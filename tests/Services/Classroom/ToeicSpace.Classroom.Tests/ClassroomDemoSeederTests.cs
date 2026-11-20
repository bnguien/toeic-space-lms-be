using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToeicSpace.Classroom.Application;
using ToeicSpace.Classroom.Application.Interfaces;
using ToeicSpace.Classroom.Application.Interfaces.Repositories;
using ToeicSpace.Classroom.Application.Interfaces.Services;
using ToeicSpace.Classroom.Domain.Entities;
using ToeicSpace.Classroom.Infrastructure.Persistence;
using ToeicSpace.Classroom.Infrastructure.Persistence.Repositories;
using ToeicSpace.Classroom.Infrastructure.Persistence.Seeding;
using ToeicSpace.Classroom.Infrastructure.Services.Scheduling;
using Xunit;

namespace ToeicSpace.Classroom.Tests;

public sealed class ClassroomDemoSeederTests
{
    [Fact]
    public async Task Seed_is_idempotent_and_creates_consistent_demo_data()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices();
        services.AddDbContext<ClassroomDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<IClassRepository, ClassRepository>();
        services.AddScoped<IFacilityRepository, FacilityRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITeacherReadRepository, TeacherReadRepository>();
        services.AddScoped<IStudentReadRepository, StudentReadRepository>();
        services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
        services.AddScoped<IClassSchedulingService, ClassSchedulingService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClassroomDbContext>();
        for (var i = 0; i < 3; i++)
            db.Teachers.Add(new TeacherReadModel { TeacherId = Guid.NewGuid(), Name = $"Teacher {i}", Email = $"teacher{i}@demo.test",
                Role = "Teacher", Status = "Active", IsEligible = true, Version = 1, SynchronizedAt = DateTime.UtcNow });
        for (var i = 0; i < 12; i++)
            db.Students.Add(new StudentReadModel { StudentId = Guid.NewGuid(), Name = $"Student {i}", Email = $"student{i}@demo.test",
                Role = "User", Status = "Active", IsEligible = true, IsVerified = true, Version = 1, SynchronizedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var seeder = new ClassroomDemoSeeder(db, scope.ServiceProvider.GetRequiredService<MediatR.ISender>(),
            scope.ServiceProvider.GetRequiredService<IHostEnvironment>(), TimeProvider.System,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));
        var first = await seeder.SeedAsync();
        var countsAfterFirst = await CountsAsync(db);
        var second = await seeder.SeedAsync();
        var countsAfterSecond = await CountsAsync(db);

        Assert.Equal(countsAfterFirst, countsAfterSecond);
        Assert.Equal((2, 5, 4, 6, 36), countsAfterSecond);
        Assert.Equal(12, second.Enrollments);
        Assert.Equal(0, second.CreatedPrimaryRecords);
        Assert.InRange(second.ReusedRecords, 2, int.MaxValue);
        Assert.Equal(36, second.SkippedOccurrences);
        Assert.Equal(4, await db.ClassInstructors.CountAsync(x => x.UnassignedAt == null));
        Assert.Equal(39, await db.ClassSessionAudits.CountAsync());
        var sessions = await db.ClassSessions.ToListAsync();
        Assert.Contains(sessions, session => session.Status == ToeicSpace.Classroom.Domain.Enums.SessionStatus.Cancelled);
        Assert.Contains(sessions, session => session.ModeOverridden);
        Assert.Contains(sessions, session => session.RoomOverridden);
        Assert.Contains(sessions, session => session.Mode == ToeicSpace.Classroom.Domain.Enums.SessionMode.Online);
        Assert.Contains(sessions, session => session.Mode == ToeicSpace.Classroom.Domain.Enums.SessionMode.Offline);
        Assert.All(sessions, session =>
        {
            Assert.Equal(DateTimeKind.Utc, session.StartAt.Kind);
            Assert.Equal(DateTimeKind.Utc, session.EndAt.Kind);
            Assert.True(session.StartAt < session.EndAt);
        });
    }

    [Fact]
    public async Task Seed_fails_before_writing_when_external_projections_are_missing()
    {
        var options = new DbContextOptionsBuilder<ClassroomDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ClassroomDbContext(options);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices();
        services.AddDbContext<ClassroomDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sender = services.BuildServiceProvider().GetRequiredService<MediatR.ISender>();
        var seeder = new ClassroomDemoSeeder(db, sender, new TestHostEnvironment(), TimeProvider.System,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync());

        Assert.Contains("TeacherReadModels", error.Message);
        Assert.Empty(await db.Centers.ToListAsync());
    }

    private static async Task<(int Centers, int Rooms, int Classes, int Schedules, int Sessions)> CountsAsync(ClassroomDbContext db) =>
        (await db.Centers.CountAsync(), await db.Rooms.CountAsync(), await db.Classes.CountAsync(),
            await db.ClassSchedules.CountAsync(), await db.ClassSessions.CountAsync());

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Classroom.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
