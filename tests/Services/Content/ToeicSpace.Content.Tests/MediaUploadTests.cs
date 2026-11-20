using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using ToeicSpace.Content.API.Controllers;
using ToeicSpace.Content.Application;
using ToeicSpace.Content.Application.Features.Lessons.Commands.PresignLessonUpload;
using ToeicSpace.Content.Application.Features.Lessons.Commands.ConfirmLessonUpload;
using ToeicSpace.Content.Application.DTOs.Lessons;
using ToeicSpace.Content.Application.Interfaces.Services;
using ToeicSpace.Content.Application.Interfaces;
using ToeicSpace.Content.Application.Interfaces.Repositories;
using ToeicSpace.Content.Application.Services;
using ToeicSpace.Content.Domain.Entities;
using ToeicSpace.Content.Domain.Enums;
using ToeicSpace.Content.Domain.Exceptions;
using ToeicSpace.Content.Infrastructure.Persistence;
using ToeicSpace.Content.Infrastructure.Persistence.Repositories;
using ToeicSpace.Content.Infrastructure.Services;
using Xunit;

namespace ToeicSpace.Content.Tests;

public sealed class MediaUploadTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        private ServiceProvider _provider = null!;
        public ContentDbContext Db { get; private set; } = null!;
        public LessonRepository Lessons => new(Db);
        public LessonUploadRepository Uploads => new(Db);
        public UnitOfWork Uow => new(Db);
        public FakeMediaStore Media { get; } = new();
        public ISender Sender => _provider.GetRequiredService<ISender>();

        public async Task SetupAsync()
        {
            await _connection.OpenAsync();
            Db = new ContentDbContext(new DbContextOptionsBuilder<ContentDbContext>().UseSqlite(_connection).Options);
            await Db.Database.EnsureCreatedAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddApplication();
            services.AddSingleton(Db);
            services.AddSingleton<ILessonMediaStore>(Media);
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            services.AddScoped<ILessonRepository, LessonRepository>();
            services.AddScoped<ILessonUploadRepository, LessonUploadRepository>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            _provider = services.BuildServiceProvider();
        }

        public PresignLessonUploadHandler Presign => new(Uploads, Media, Uow, TimeProvider.System);
        public ConfirmLessonUploadHandler Confirm => new(Uploads, Media, Uow, TimeProvider.System);
        public async ValueTask DisposeAsync() { await _provider.DisposeAsync(); await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    [Fact]
    public void Real_R2_media_signature_checks_reject_spoofed_files()
    {
        Assert.True(R2LessonMediaStore.HasValidSignature("video/mp4", [0, 0, 0, 12, 102, 116, 121, 112]));
        Assert.True(R2LessonMediaStore.HasValidSignature("application/pdf", "%PDF-1.7"u8));
        Assert.True(R2LessonMediaStore.HasValidSignature("image/png", [137, 80, 78, 71, 13, 10, 26, 10]));
        Assert.False(R2LessonMediaStore.HasValidSignature("video/mp4", "<script>"u8));
        Assert.False(R2LessonMediaStore.HasValidSignature("application/pdf", "<html>"u8));
    }

    [Fact]
    public async Task Uploads_validate_type_size_signature_owner_and_expiration()
    {
        await using var f = new Fixture();
        await f.SetupAsync();
        Assert.Equal("invalid_media", (await Assert.ThrowsAsync<ContentRuleException>(() =>
            f.Presign.Handle(new(LessonUploadKind.Video, "clip.avi", "video/x-msvideo", 100, Admin), default))).Code);
        Assert.Equal("invalid_media", (await Assert.ThrowsAsync<ContentRuleException>(() =>
            f.Presign.Handle(new(LessonUploadKind.Attachment, "too-large.pdf", "application/pdf",
                20L * 1024 * 1024 + 1, Admin), default))).Code);
        var upload = await f.Presign.Handle(new(LessonUploadKind.Video, "clip.mp4", "video/mp4", 100, Admin), default);
        Assert.StartsWith($"lms/media/{Admin:N}/", upload.Key);
        Assert.Equal("upload_not_found", (await Assert.ThrowsAsync<ContentRuleException>(() =>
            f.Confirm.Handle(new(upload.Key, Guid.NewGuid()), default))).Code);
        Assert.Equal("upload_missing", (await Assert.ThrowsAsync<ContentRuleException>(() =>
            f.Confirm.Handle(new(upload.Key, Admin), default))).Code);
        f.Media.Objects[upload.Key] = new StoredThumbnail("video/mp4", 100, false);
        Assert.Equal("upload_mismatch", (await Assert.ThrowsAsync<ContentRuleException>(() =>
            f.Confirm.Handle(new(upload.Key, Admin), default))).Code);
        f.Media.Objects[upload.Key] = new StoredThumbnail("video/mp4", 100, true);
        Assert.Equal(upload.Key, (await f.Confirm.Handle(new(upload.Key, Admin), default)).Key);
        var expired = await f.Presign.Handle(new(LessonUploadKind.Attachment, "file.pdf", "application/pdf", 10, Admin), default);
        (await f.Uploads.GetOwnedAsync(expired.Key, Admin, default))!.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await f.Db.SaveChangesAsync();
        Assert.Equal("upload_expired", (await Assert.ThrowsAsync<ContentRuleException>(() =>
            f.Confirm.Handle(new(expired.Key, Admin), default))).Code);
    }

    [Fact]
    public async Task Single_batch_upload_request_accepts_multiple_videos_and_attachments()
    {
        await using var f = new Fixture();
        await f.SetupAsync();
        var files = new LessonUploadRequest[]
        {
            new(LessonUploadKind.Video, "first.mp4", "video/mp4", 1000),
            new(LessonUploadKind.Video, "second.mp4", "video/mp4", 1200),
            new(LessonUploadKind.Attachment, "guide.pdf", "application/pdf", 200),
            new(LessonUploadKind.Attachment, "image.webp", "image/webp", 300)
        };
        var presign = await f.Sender.Send(new PresignLessonUploadsCommand(files, Admin));
        Assert.Equal(4, presign.PresignedCount);
        for (var i = 0; i < files.Length; i++)
            f.Media.Objects[presign.Items[i].Upload!.Key] = new StoredThumbnail(files[i].ContentType, files[i].SizeBytes, true);
        var keys = presign.Items.Select(x => x.Upload!.Key).ToArray();
        var confirm = await f.Sender.Send(new ConfirmLessonUploadsCommand(keys, Admin));
        Assert.Equal(4, confirm.ConfirmedCount);
    }

    private sealed class FakeMediaStore : ILessonMediaStore
    {
        public Dictionary<string, StoredThumbnail> Objects { get; } = [];
        public List<string> Deleted { get; } = [];
        public TimeSpan LastReadLifetime { get; private set; }
        public Task<string> PresignUploadAsync(string key, string contentType, TimeSpan lifetime, CancellationToken ct) =>
            Task.FromResult($"https://upload.example/{key}");
        public Task<StoredThumbnail?> HeadAsync(string key, CancellationToken ct) =>
            Task.FromResult(Objects.TryGetValue(key, out var info) ? info : null);
        public Task<string> PresignReadAsync(string key, TimeSpan lifetime, CancellationToken ct)
        {
            LastReadLifetime = lifetime;
            return Task.FromResult($"https://private.example/{key}");
        }
        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Deleted.Add(key);
            Objects.Remove(key);
            return Task.CompletedTask;
        }
    }

}
