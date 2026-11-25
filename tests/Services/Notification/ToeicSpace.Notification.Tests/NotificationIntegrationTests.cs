using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ToeicSpace.BuildingBlocks.Messaging.Events;
using ToeicSpace.BuildingBlocks.Pagination;
using ToeicSpace.BuildingBlocks.Security.Jwt;
using ToeicSpace.Notification.Application.DTOs;
using ToeicSpace.Notification.Application.Features.Notifications.Commands.CreateNotification;
using ToeicSpace.Notification.Infrastructure.Persistence;
using Xunit;

namespace ToeicSpace.Notification.Tests;

public sealed class NotificationIntegrationTests
{
    private const string DatabaseVariable = "NOTIFICATION_TEST_MYSQL";
    private const string BrokerVariable = "NOTIFICATION_TEST_RABBITMQ_HOST";
    private const string ApiPath = "/api/v1/notifications";

    [NotificationIntegrationFact]
    public async Task MigratesConsumesConcurrentEventsAndIsolatesRestAndSignalR()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signingKey = new ECDsaSecurityKey(key) { KeyId = EcdsaKeyLoader.ComputeKeyId(key) };

        var redis = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_REDIS");
        var prefix = $"notification-tests-{Guid.NewGuid():N}";
        WebApplication BuildInstance() => NotificationApi.Build([], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NotificationDatabase"] = Environment.GetEnvironmentVariable(DatabaseVariable),
                ["RabbitMQ:Host"] = Environment.GetEnvironmentVariable(BrokerVariable),
                ["RabbitMQ:Username"] = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_RABBITMQ_USER") ?? "guest",
                ["RabbitMQ:Password"] = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_RABBITMQ_PASSWORD") ?? "guest",
                ["SignalR:Mode"] = redis is null ? "SingleInstance" : "Redis",
                ["ConnectionStrings:Redis"] = redis,
                ["SignalR:ChannelPrefix"] = prefix,
                ["Jwt:Issuer"] = "notification-integration",
                ["Jwt:Audience"] = "notification-integration",
                ["Jwt:PublicKey"] = key.ExportSubjectPublicKeyInfoPem()
            });
            builder.Services.Configure<MassTransitHostOptions>(options => options.WaitUntilStarted = true);
            builder.WebHost.UseUrls("http://127.0.0.1:0");
        });

        await using var app = BuildInstance();
        await using var replica = redis is null ? null : BuildInstance();
        await using (var migrations = app.Services.CreateAsyncScope())
        {
            var db = migrations.ServiceProvider.GetRequiredService<NotificationDbContext>();
            await db.Database.MigrateAsync(ct);
        }
        await app.StartAsync(ct);

        if (replica is not null)
            await replica.StartAsync(ct);
        var secondAddress = new Uri((replica ?? app).Urls.Single());
        var address = new Uri(app.Urls.Single());
        var student = Guid.NewGuid();
        var teacher = Guid.NewGuid();
        var offlineAdmin = Guid.NewGuid();
        using var anonymous = new HttpClient { BaseAddress = address };
        using var studentApi = Client(address, Token(student, "User", signingKey));
        using var teacherApi = Client(address, Token(teacher, "Teacher", signingKey));
        using var adminApi = Client(address, Token(offlineAdmin, "Admin", signingKey));

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(ApiPath, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync("/hubs/notifications/negotiate", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await studentApi.GetAsync(ApiPath + "?pageSize=0", ct)).StatusCode);

        using var studentHub = await ConnectAsync(address, Token(student, "User", signingKey), ct);
        using var secondStudentTab = await ConnectAsync(secondAddress, Token(student, "User", signingKey), ct);
        using var teacherHub = await ConnectAsync(secondAddress, Token(teacher, "Teacher", signingKey), ct);
        var bus = app.Services.GetRequiredService<IBus>();
        var attempt = new ExamAttemptSubmittedIntegrationEvent(Guid.NewGuid(), student, Guid.NewGuid(),
            "TEST", "Đề thi thử", "", 100, 100, 200, 50, 100, 30, DateTime.UtcNow);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => bus.Publish(attempt, ct)));

        var studentMessages = new SignalRMessages(studentHub);
        using var studentMessage = await studentMessages.ReceiveAsync("NotificationCreated", ct);
        using var secondTabMessage = await new SignalRMessages(secondStudentTab).ReceiveAsync("NotificationCreated", ct);
        using var countMessage = await studentMessages.ReceiveAsync("UnreadCountChanged", ct);
        var notification = studentMessage.RootElement.GetProperty("arguments")[0];
        var id = notification.GetProperty("id").GetGuid();
        Assert.Equal(id, secondTabMessage.RootElement.GetProperty("arguments")[0].GetProperty("id").GetGuid());
        Assert.Equal("Đã có kết quả bài thi", notification.GetProperty("title").GetString());
        Assert.Equal(1, countMessage.RootElement.GetProperty("arguments")[0].GetProperty("count").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await teacherApi.GetAsync($"{ApiPath}/{id}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminApi.PatchAsync($"{ApiPath}/{id}/read", null, ct)).StatusCode);

        var account = new ActiveAccountCreatedIntegrationEvent(offlineAdmin, DateTime.UtcNow);
        await bus.Publish(account, ct);
        var offline = await WaitForNotificationsAsync(adminApi, ct);
        Assert.Equal("ActiveAccountCreated", Assert.Single(offline.Items).Type);

        var command = new CreateNotificationCommand([student, teacher], "Lịch học", "Nội dung tiếng Việt",
            "Test", "Integration", Guid.NewGuid(), null, "/classes");
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, ct);
        }));

        // Resume a fan-out after only the first recipient of a second type was committed.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var reminder = command with { Type = "Reminder", RecipientIds = [teacher, offlineAdmin] };
            await sender.Send(reminder with { RecipientIds = [teacher] }, ct);
            await sender.Send(reminder, ct);
            await sender.Send(reminder, ct);
            await sender.Send(reminder with { SourceEventId = Guid.NewGuid(), RecipientIds = [offlineAdmin] }, ct);
        }
        Assert.Equal(1, (await teacherApi.GetFromJsonAsync<PaginatedResult<NotificationDto>>(
            ApiPath + "?type=Reminder", ct))!.TotalCount);
        Assert.Equal(2, (await adminApi.GetFromJsonAsync<PaginatedResult<NotificationDto>>(
            ApiPath + "?type=Reminder", ct))!.TotalCount);

        using var teacherMessage = await new SignalRMessages(teacherHub).ReceiveAsync("NotificationCreated", ct);
        Assert.Equal("Test", teacherMessage.RootElement.GetProperty("arguments")[0].GetProperty("type").GetString());

        var page = await studentApi.GetFromJsonAsync<PaginatedResult<NotificationDto>>(
            ApiPath + "?pageSize=1&isRead=false", ct);
        Assert.Equal(2, page!.TotalCount);
        Assert.True(page.HasNextPage);
        Assert.Equal("Nội dung tiếng Việt", Assert.Single(page.Items).Body);
        Assert.Equal(1, (await studentApi.GetFromJsonAsync<PaginatedResult<NotificationDto>>(
            ApiPath + "?type=ExamAttemptScored", ct))!.TotalCount);

        Assert.Equal(HttpStatusCode.NoContent, (await studentApi.PatchAsync($"{ApiPath}/{id}/read", null, ct)).StatusCode);
        var initialRead = await studentApi.GetFromJsonAsync<NotificationDto>($"{ApiPath}/{id}", ct);
        Assert.NotNull(initialRead!.ReadAt);
        Assert.Equal(HttpStatusCode.NoContent, (await studentApi.PatchAsync($"{ApiPath}/{id}/read", null, ct)).StatusCode);
        var repeatedRead = await studentApi.GetFromJsonAsync<NotificationDto>($"{ApiPath}/{id}", ct);
        Assert.Equal(initialRead.ReadAt, repeatedRead!.ReadAt);
        Assert.Equal(HttpStatusCode.NoContent, (await studentApi.PatchAsync($"{ApiPath}/read-all", null, ct)).StatusCode);
        Assert.Equal(0, (await studentApi.GetFromJsonAsync<UnreadCountDto>($"{ApiPath}/unread-count", ct))!.Count);
        Assert.Equal(2, (await teacherApi.GetFromJsonAsync<UnreadCountDto>($"{ApiPath}/unread-count", ct))!.Count);
        if (replica is not null)
        {
            // Pin the application handler to each host while both RabbitMQ consumers remain active.
            var other = Guid.NewGuid();
            using var listenerA = await ConnectAsync(address, Token(other, "Teacher", signingKey), ct);
            using var listenerB = await ConnectAsync(secondAddress, Token(other, "Teacher", signingKey), ct);
            var messagesA = new SignalRMessages(listenerA);
            var messagesB = new SignalRMessages(listenerB);
            async Task SendFrom(WebApplication host, Guid eventId, string title)
            {
                await using var scope = host.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(command with
                {
                    RecipientIds = [other], SourceEventId = eventId, Title = title
                }, ct);
            }
            foreach (var (host, title) in new[] { (app, "From A"), (replica, "From B") })
            {
                await SendFrom(host, Guid.NewGuid(), title);
                foreach (var listener in new[] { messagesA, messagesB })
                {
                    using var message = await listener.ReceiveAsync("NotificationCreated", ct);
                    Assert.Equal(title, message.RootElement.GetProperty("arguments")[0].GetProperty("title").GetString());
                }
            }

            var container = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_REDIS_CONTAINER");
            if (!string.IsNullOrEmpty(container))
            {
                using var reader = Client(secondAddress, Token(other, "Teacher", signingKey));
                var eventId = Guid.NewGuid();
                await ControlRedisAsync("stop", container, ct);
                try
                {
                    await SendFrom(app, eventId, "Offline");
                    await SendFrom(replica, eventId, "Offline");
                    var outagePage = await reader.GetFromJsonAsync<PaginatedResult<NotificationDto>>(ApiPath, ct);
                    Assert.Equal(3, outagePage!.TotalCount);
                    Assert.Single(outagePage.Items, item => item.Title == "Offline");
                }
                finally
                {
                    await ControlRedisAsync("start", container, CancellationToken.None);
                }

                while ((await reader.GetStringAsync("/health", ct)) != "Healthy")
                    await Task.Delay(200, ct);

                using var reconnected = await ConnectAsync(secondAddress, Token(other, "Teacher", signingKey), ct);
                Assert.Equal(3, (await reader.GetFromJsonAsync<UnreadCountDto>(ApiPath + "/unread-count", ct))!.Count);
                await SendFrom(app, Guid.NewGuid(), "Recovered");
                using var recovered = await new SignalRMessages(reconnected).ReceiveAsync("NotificationCreated", ct);
                Assert.Equal("Recovered", recovered.RootElement.GetProperty("arguments")[0].GetProperty("title").GetString());
            }
        }
    }

    private static async Task ControlRedisAsync(string action, string container, CancellationToken ct)
    {
        var start = new System.Diagnostics.ProcessStartInfo("docker") { UseShellExecute = false };
        start.ArgumentList.Add(action);
        start.ArgumentList.Add(container);
        using var process = System.Diagnostics.Process.Start(start)!;
        await process.WaitForExitAsync(ct);
        Assert.Equal(0, process.ExitCode);
    }

    private static HttpClient Client(Uri address, string token)
    {
        var client = new HttpClient { BaseAddress = address };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Token(Guid id, string role, SecurityKey key)
    {
        var token = new JwtSecurityTokenHandler().CreateJwtSecurityToken(new SecurityTokenDescriptor
        {
            Issuer = "notification-integration",
            Audience = "notification-integration",
            TokenType = AccessTokenDefaults.TokenType,
            Subject = new ClaimsIdentity([new Claim("sub", id.ToString()), new Claim("role", role)]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key, AccessTokenDefaults.Algorithm)
        });
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<ClientWebSocket> ConnectAsync(Uri address, string token, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        try
        {
            var uri = new UriBuilder(address) { Scheme = "ws", Path = "/hubs/notifications", Query = $"access_token={token}" };
            await socket.ConnectAsync(uri.Uri, ct);
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),
                WebSocketMessageType.Text, true, ct);
            var handshake = await ReadFrameAsync(socket, ct);
            Assert.StartsWith("{}\u001e", handshake);
            // The handshake precedes OnConnectedAsync. Wait for the dispatcher so Redis
            // user subscriptions are ready before publishing the first test event.
            var probe = JsonSerializer.Serialize(new { type = 1, invocationId = "ready", target = "TestReady", arguments = Array.Empty<object>() });
            await socket.SendAsync(Encoding.UTF8.GetBytes(probe + "\u001e"), WebSocketMessageType.Text, true, ct);
            while (true)
            {
                var frame = await ReadFrameAsync(socket, ct);
                foreach (var payload in frame.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
                {
                    using var message = JsonDocument.Parse(payload);
                    if (message.RootElement.GetProperty("type").GetInt32() == 3 &&
                        message.RootElement.GetProperty("invocationId").GetString() == "ready")
                        return socket;
                }
            }
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private sealed class SignalRMessages(ClientWebSocket socket)
    {
        private readonly Queue<string> _pending = new();

        public async Task<JsonDocument> ReceiveAsync(string target, CancellationToken ct)
        {
            while (true)
            {
                if (_pending.Count == 0)
                {
                    var frame = await ReadFrameAsync(socket, ct);
                    foreach (var payload in frame.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
                        _pending.Enqueue(payload);
                }

                if (!_pending.TryDequeue(out var next))
                    continue;

                var message = JsonDocument.Parse(next);
                if (message.RootElement.TryGetProperty("target", out var actualTarget)
                    && actualTarget.GetString() == target)
                    return message;

                message.Dispose();
            }
        }
    }

    private static async Task<string> ReadFrameAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task<PaginatedResult<NotificationDto>> WaitForNotificationsAsync(HttpClient client, CancellationToken ct)
    {
        while (true)
        {
            var page = await client.GetFromJsonAsync<PaginatedResult<NotificationDto>>(ApiPath, ct);
            if (page!.TotalCount > 0)
                return page;

            await Task.Delay(100, ct);
        }
    }

    public sealed class NotificationIntegrationFactAttribute : FactAttribute
    {
        public NotificationIntegrationFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DatabaseVariable))
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BrokerVariable)))
                Skip = "Requires a dedicated MySQL database and RabbitMQ instance.";
        }
    }
}
