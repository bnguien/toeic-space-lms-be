using Microsoft.EntityFrameworkCore;
using System.Data;
using MySqlConnector;
using ToeicSpace.Classroom.Application.DTOs.Scheduling;
using ToeicSpace.Classroom.Domain.Entities;
using ToeicSpace.Classroom.Domain.Enums;
using ToeicSpace.Classroom.Domain.Exceptions;
using ToeicSpace.Classroom.Infrastructure.Persistence;
using ToeicSpace.Classroom.Infrastructure.Services.Scheduling;
using Xunit;

namespace ToeicSpace.Classroom.Tests;

public sealed class SchedulingMySqlFactAttribute : FactAttribute
{
    public SchedulingMySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLASSROOM_SCHEDULING_TEST_DB")))
            Skip = "MySQL concurrency unverified: configure CLASSROOM_SCHEDULING_TEST_DB for a disposable *_test schema.";
    }
}

// Supply CLASSROOM_SCHEDULING_TEST_DB pointing ONLY to a disposable *_test schema.
// Without MySQL, the in-memory suite runs; these scenarios remain UNVERIFIED.
public sealed class SchedulingMySqlConcurrencyTests
{
    private static string? Connection => Environment.GetEnvironmentVariable("CLASSROOM_SCHEDULING_TEST_DB");
    private static ClassroomDbContext Db()
    {
        if (Connection is null || !new MySqlConnectionStringBuilder(Connection).Database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only a disposable *_test Classroom schema is allowed.");
        return new ClassroomDbContext(new DbContextOptionsBuilder<ClassroomDbContext>()
            .UseMySql(Connection, new MySqlServerVersion(new Version(8, 4, 0))).Options);
    }

    private static ClassSchedulingService Service(ClassroomDbContext db) => new(db, TimeProvider.System,
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));

    private static async Task<(Guid A, Guid B, Guid TeacherA, Guid TeacherB, Guid RoomA, Guid RoomB)> SeedAsync(
        bool sameTeacher = false, bool sameRoom = false)
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        var center = new Center { Name = "Test", Address = "Test address", CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow };
        var roomA = new Room { Center = center, Name = Guid.NewGuid().ToString("N"), Capacity = 30 };
        var roomB = new Room { Center = center, Name = Guid.NewGuid().ToString("N"), Capacity = 30 };
        db.Rooms.AddRange(roomA, roomB);
        var teacherA = Guid.NewGuid();
        var teacherB = sameTeacher ? teacherA : Guid.NewGuid();
        db.Teachers.Add(new TeacherReadModel { TeacherId = teacherA, Name = "Teacher A", Role = "Teacher", Status = "Active", IsEligible = true });
        if (!sameTeacher) db.Teachers.Add(new TeacherReadModel { TeacherId = teacherB, Name = "Teacher B", Role = "Teacher", Status = "Active", IsEligible = true });
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var to = from.AddDays(14);
        var a = new Class { Code = Guid.NewGuid().ToString("N"), Name = "A", Status = ClassStatus.Active,
            StartDate = from, EndDate = to, DefaultRoomId = roomA.Id };
        var b = new Class { Code = Guid.NewGuid().ToString("N"), Name = "B", Status = ClassStatus.Active,
            StartDate = from, EndDate = to, DefaultRoomId = sameRoom ? roomA.Id : roomB.Id };
        db.Classes.AddRange(a, b);
        db.ClassInstructors.AddRange(new ClassInstructor { ClassId = a.Id, TeacherId = teacherA },
            new ClassInstructor { ClassId = b.Id, TeacherId = teacherB });
        await db.SaveChangesAsync();
        return (a.Id, b.Id, teacherA, teacherB, roomA.Id, sameRoom ? roomA.Id : roomB.Id);
    }

    private static DateTime Start => DateTime.UtcNow.Date.AddDays(2).AddHours(9);
    private static SessionInput Input(Guid classId, Guid teacher, Guid room, DateTime? start = null) =>
        new(classId, teacher, "Test", null, SessionType.Regular, start ?? Start,
            (start ?? Start).AddHours(1), SessionMode.Offline, room, null);

    private static async Task<bool> TryAsync(Func<ClassSchedulingService, Task> action)
    {
        await using var db = Db();
        try { await action(Service(db)); return true; }
        catch (ClassRuleException ex) when (ex.Code is "schedule_conflict" or "concurrency_conflict" or "unique_constraint") { return false; }
        catch (MySqlException ex) when (ex.Number is 1205 or 1213 or 1062) { return false; }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: 1062 or 1205 or 1213 }) { return false; }
    }

    [SchedulingMySqlFact]
    public async Task Same_teacher_overlapping_only_one_succeeds()
    {
        if (Connection is null) return; // Scenario is unverified without a disposable MySQL schema.
        var s = await SeedAsync(sameTeacher: true);
        var outcomes = await Task.WhenAll(
            TryAsync(service => service.CreateAsync(Input(s.A, s.TeacherA, s.RoomA), Guid.NewGuid(), true, default)),
            TryAsync(service => service.CreateAsync(Input(s.B, s.TeacherB, s.RoomB), Guid.NewGuid(), true, default)));
        Assert.Single(outcomes, x => x);
    }

    [SchedulingMySqlFact]
    public async Task Same_room_different_teachers_only_one_succeeds()
    {
        if (Connection is null) return; // Scenario is unverified without a disposable MySQL schema.
        var s = await SeedAsync(sameRoom: true);
        var outcomes = await Task.WhenAll(
            TryAsync(service => service.CreateAsync(Input(s.A, s.TeacherA, s.RoomA), Guid.NewGuid(), true, default)),
            TryAsync(service => service.CreateAsync(Input(s.B, s.TeacherB, s.RoomB), Guid.NewGuid(), true, default)));
        Assert.Single(outcomes, x => x);
    }

    [SchedulingMySqlFact]
    public async Task Different_teachers_and_rooms_may_overlap()
    {
        if (Connection is null) return; // Scenario is unverified without a disposable MySQL schema.
        var s = await SeedAsync();
        var outcomes = await Task.WhenAll(
            TryAsync(service => service.CreateAsync(Input(s.A, s.TeacherA, s.RoomA), Guid.NewGuid(), true, default)),
            TryAsync(service => service.CreateAsync(Input(s.B, s.TeacherB, s.RoomB), Guid.NewGuid(), true, default)));
        Assert.All(outcomes, Assert.True);
    }

    [SchedulingMySqlFact]
    public async Task Creation_and_reschedule_conflict_across_classes()
    {
        if (Connection is null) return; // Scenario is unverified without a disposable MySQL schema.
        var s = await SeedAsync(sameTeacher: true);
        await using var db = Db();
        var existing = await Service(db).CreateAsync(Input(s.A, s.TeacherA, s.RoomA, Start.AddHours(3)), Guid.NewGuid(), true, default);
        var change = new SessionEdit(s.TeacherA, "Moved", null, Start, Start.AddHours(1),
            SessionMode.Offline, s.RoomA, null, "Reschedule", existing.Version);
        var outcomes = await Task.WhenAll(
            TryAsync(service => service.UpdateAsync(existing.Id, change, Guid.NewGuid(), true, default)),
            TryAsync(service => service.CreateAsync(Input(s.B, s.TeacherB, s.RoomB), Guid.NewGuid(), true, default)));
        Assert.Single(outcomes, x => x);
    }

    [SchedulingMySqlFact]
    public async Task Concurrent_generation_is_idempotent()
    {
        if (Connection is null) return; // Scenario is unverified without a disposable MySQL schema.
        var s = await SeedAsync();
        await using var db = Db();
        var date = DateOnly.FromDateTime(Start.AddHours(7));
        await Service(db).CreateScheduleAsync(new ScheduleInput(s.A, [date.DayOfWeek], new TimeOnly(16, 0),
            new TimeOnly(17, 0), s.TeacherA, SessionMode.Online, "Asia/Ho_Chi_Minh", date, date, []),
            Guid.NewGuid(), default);
        var results = await Task.WhenAll(
            TryAsync(service => service.GenerateAsync(s.A, new(date, date, null), Guid.NewGuid(), default)),
            TryAsync(service => service.GenerateAsync(s.A, new(date, date, null), Guid.NewGuid(), default)));
        Assert.All(results, Assert.True);
        Assert.Equal(1, await db.ClassSessions.CountAsync(x => x.ClassId == s.A));
    }

    [SchedulingMySqlFact]
    public async Task Concurrent_same_session_version_only_one_succeeds()
    {
        if (Connection is null) return; // Scenario is unverified without a disposable MySQL schema.
        var s = await SeedAsync();
        await using var db = Db();
        var existing = await Service(db).CreateAsync(Input(s.A, s.TeacherA, s.RoomA), Guid.NewGuid(), true, default);
        var edit = new SessionEdit(s.TeacherA, "Changed", null, existing.StartAt.AddMinutes(15),
            existing.EndAt.AddMinutes(15), SessionMode.Offline, s.RoomA, null, "Adjustment", existing.Version);
        var results = await Task.WhenAll(
            TryAsync(service => service.UpdateAsync(existing.Id, edit, Guid.NewGuid(), true, default)),
            TryAsync(service => service.UpdateAsync(existing.Id, edit with { Title = "Other" }, Guid.NewGuid(), true, default)));
        Assert.Single(results, x => x);
    }

    [SchedulingMySqlFact]
    public async Task Default_room_change_and_session_override_do_not_lose_each_other()
    {
        if (Connection is null) return; // Scenario is unverified without a disposable MySQL schema.
        var s = await SeedAsync();
        await using var db = Db();
        var existing = await Service(db).CreateAsync(Input(s.A, s.TeacherA, s.RoomA), Guid.NewGuid(), true, default);
        var edit = new SessionEdit(s.TeacherA, "Override", null, existing.StartAt, existing.EndAt,
            SessionMode.Offline, s.RoomB, null, "Individual room", existing.Version);
        var results = await Task.WhenAll(
            TryAsync(async service =>
            {
                await using var context = Db();
                // The handler is tested separately with an in-memory transaction; this scenario
                // checks resource locking of the propagation path under the same Class row lock.
                await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                var cls = await context.Classes.FromSqlInterpolated($"SELECT * FROM Classes WHERE Id = {s.A} FOR UPDATE").SingleAsync();
                var scheduler = Service(context);
                await scheduler.PrepareDefaultRoomChangeAsync(s.A, s.RoomB, DateTime.UtcNow, default);
                await scheduler.ApplyDefaultRoomAsync(s.A, s.RoomB, Guid.NewGuid(), "New default", DateTime.UtcNow, default);
                cls.DefaultRoomId = s.RoomB;
                cls.Touch(Guid.NewGuid(), DateTime.UtcNow);
                await context.SaveChangesAsync();
                await tx.CommitAsync();
            }),
            TryAsync(service => service.UpdateAsync(existing.Id, edit, Guid.NewGuid(), true, default)));
        Assert.Contains(results, x => x);
        var persisted = await db.ClassSessions.AsNoTracking().SingleAsync(x => x.Id == existing.Id);
        Assert.Equal(s.RoomB, persisted.RoomId);
    }
}
