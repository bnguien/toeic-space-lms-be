using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using ToeicSpace.BuildingBlocks.Security.Extensions;
using ToeicSpace.Notification.API.Hubs;
using ToeicSpace.Notification.API.Middlewares;
using ToeicSpace.Notification.API.Security;
using ToeicSpace.Notification.Application;
using ToeicSpace.Notification.Application.Interfaces;
using ToeicSpace.Notification.Infrastructure;
using ToeicSpace.Notification.Infrastructure.Persistence;

var app = NotificationApi.Build(args);
app.Run();

public static class NotificationApi
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        // Align controller discovery for executable and in-process integration tests.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(NotificationApi).Assembly.GetName().Name
        });
        configure?.Invoke(builder);
        // Browser transports can put the access token in the query string.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.Services.AddApplicationServices();
        builder.Services.AddInfrastructureServices(builder.Configuration);
        builder.Services.AddExceptionHandler<NotificationExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddKestrelHardening();
        builder.Services.AddTrustedForwardedHeaders(builder.Configuration);
        builder.Services.AddToeicSpaceJwtAuthentication(builder.Configuration);
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Events.OnMessageReceived = context =>
            {
                if (context.Request.Path.Equals(NotificationHub.Path, StringComparison.OrdinalIgnoreCase)
                    && !context.Request.Headers.ContainsKey("Authorization")
                    && context.Request.Query.TryGetValue("access_token", out var token) && token.Count == 1)
                {
                    context.Token = token[0];
                    // Authentication retains the token; later components only see the remaining query.
                    context.Request.Query = new QueryCollection(context.Request.Query
                        .Where(pair => !pair.Key.Equals("access_token", StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
                }
                return Task.CompletedTask;
            };
        });
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(NotificationIdentity.Policy, policy => policy.RequireAuthenticatedUser()
                .RequireRole("Admin", "Teacher", "User", "Student")
                .RequireAssertion(context => NotificationIdentity.UserId(context.User).HasValue));
        var signalR = builder.Services.AddSignalR();
        switch (builder.Configuration["SignalR:Mode"] ?? "SingleInstance")
        {
            case "SingleInstance":
                break;
            case "Redis":
                var connection = builder.Configuration.GetConnectionString("Redis");
                var prefix = builder.Configuration["SignalR:ChannelPrefix"];
                if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(prefix))
                    throw new InvalidOperationException("Redis mode requires ConnectionStrings:Redis and SignalR:ChannelPrefix.");

                signalR.AddStackExchangeRedis(connection, options =>
                {
                    options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal(prefix);
                    options.Configuration.AbortOnConnectFail = false;
                });
                builder.Services.AddHealthChecks().AddRedis(connection, name: "notification-redis",
                    failureStatus: HealthStatus.Degraded);
                break;
            default:
                throw new InvalidOperationException("SignalR:Mode must be SingleInstance or Redis.");
        }
        builder.Services.AddSingleton<IUserIdProvider, NotificationIdentity>();
        builder.Services.AddSingleton<INotificationRealtime, SignalRNotificationRealtime>();
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });
        builder.Services.AddHealthChecks().AddCheck<NotificationDatabaseHealthCheck>("notification-mysql");

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.UseSecurityHeaders();
        app.UseExceptionHandler();
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHub<NotificationHub>(NotificationHub.Path, options => options.CloseOnAuthenticationExpiration = true);
        app.MapHealthChecks("/health").AllowAnonymous();
        return app;
    }
}


internal sealed class NotificationDatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        // Check connectivity and confirm that the Notification schema has been migrated.
        await db.Notifications.AnyAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
