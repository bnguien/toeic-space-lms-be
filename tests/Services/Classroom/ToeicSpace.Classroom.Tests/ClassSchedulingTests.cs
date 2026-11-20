using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Authorization;
using ToeicSpace.Classroom.API.Controllers;
using ToeicSpace.Classroom.Application.Features.Classes.Commands.SetDefaultRoom;
using ToeicSpace.Classroom.Application.DTOs.Scheduling;
using ToeicSpace.Classroom.Application.Interfaces;
using ToeicSpace.Classroom.Domain.Entities;
using ToeicSpace.Classroom.Domain.Enums;
using ToeicSpace.Classroom.Domain.Exceptions;
using ToeicSpace.Classroom.Infrastructure.Persistence;
using ToeicSpace.Classroom.Infrastructure.Persistence.Repositories;
using ToeicSpace.Classroom.Infrastructure.Services.Scheduling;
using Xunit;

namespace ToeicSpace.Classroom.Tests;

public sealed class ClassSchedulingTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    private static readonly Guid Admin = Guid.NewGuid();
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Value = now;
        public override DateTimeOffset GetUtcNow() => Value;
    }

    private static ClassroomDbContext Db() => new(new DbContextOptionsBuilder<ClassroomDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(ClassroomDbContext Db, ClassSchedulingService Service, Clock Clock,
        Guid ClassId, Guid TeacherId, Guid RoomId)> SetupAsync()
    {
        var db = Db();
        var clock = new Clock(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero));
        var cls = new Class { Code = Guid.NewGuid().ToString("N"), Name = "TOEIC A",
            StartDate = new DateOnly(2026, 11, 14), EndDate = new DateOnly(2026, 12, 31),
            Status = ClassStatus.Active };
        var center = new Center { Name = "Branch", Address = "Address", TimeZoneId = Zone.Id };
        var room = new Room { CenterId = center.Id, Center = center, Name = "101", Capacity = 10 };
        var teacher = Guid.NewGuid();
        cls.DefaultRoomId = room.Id;
        db.Centers.Add(center);
        db.Rooms.Add(room);
        db.Classes.Add(cls);
        db.ClassInstructors.Add(new ClassInstructor { ClassId = cls.Id, TeacherId = teacher });
        db.Teachers.Add(new TeacherReadModel { TeacherId = teacher, Name = "Teacher", Role = "Teacher",
            Status = "Active", IsEligible = true });
        await db.SaveChangesAsync();
        return (db, new ClassSchedulingService(db, clock, Zone), clock, cls.Id, teacher, room.Id);
    }

    private static SessionInput Input(Guid cls, Guid teacher, Guid room, int hour = 9) =>
        new(cls, teacher, "Listening", null, SessionType.Regular,
            new DateTime(2026, 11, 16, hour, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 16, hour + 1, 0, 0, DateTimeKind.Utc),
            SessionMode.Offline, room, null);

    [Fact]
    public async Task Conflicts_adjacency_online_and_authorization()
    {
        var (db, scheduling, _, cls, teacher, room) = await SetupAsync();
        await using (db)
        {
            var first = await scheduling.CreateAsync(Input(cls, teacher, room), Admin, true, default);
            Assert.Equal("schedule_conflict", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.CreateAsync(Input(cls, teacher, room), Admin, true, default))).Code);
            Assert.Equal("forbidden_class", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.GetAsync(first.Id, Guid.NewGuid(), "User", default))).Code);
            Assert.Equal("teacher_change_forbidden", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.CreateAsync(Input(cls, Guid.NewGuid(), room, 11), teacher, false, default))).Code);
            var adjacent = await scheduling.CreateAsync(Input(cls, teacher, room, 10), teacher, false, default);
            Assert.NotEqual(first.Id, adjacent.Id);
            var online = Input(cls, teacher, room, 11) with { Mode = SessionMode.Online, RoomId = null };
            var third = await scheduling.CreateAsync(online, teacher, false, default);
            Assert.Null(third.RoomId);
            Assert.Equal("online_room", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.CreateAsync(online with { RoomId = room, StartAt = online.StartAt.AddHours(1),
                    EndAt = online.EndAt.AddHours(1) }, teacher, false, default))).Code);
        }
    }

    [Fact]
    public async Task Recurrence_is_idempotent_excludes_dates_and_never_regenerates_cancelled()
    {
        var (db, scheduling, _, cls, teacher, _) = await SetupAsync();
        await using (db)
        {
            var input = new ScheduleInput(cls, [DayOfWeek.Monday, DayOfWeek.Wednesday], new TimeOnly(9, 0),
                new TimeOnly(10, 0), teacher, SessionMode.Online, Zone.Id,
                new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 25), [new DateOnly(2026, 11, 18)]);
            var schedule = await scheduling.CreateScheduleAsync(input, Admin, default);
            var range = new GenerateSessionsInput(input.EffectiveFrom, input.EffectiveTo, null);
            var generated = await db.ClassSessions.Where(x => x.ClassScheduleId == schedule.Id)
                .OrderBy(x => x.StartAt).ToListAsync();
            Assert.Equal(new DateOnly?[] { new(2026, 11, 16), new(2026, 11, 23), new(2026, 11, 25) },
                generated.Select(x => x.OriginalLocalDate));
            Assert.Empty((await scheduling.GenerateAsync(cls, range, Admin, default)).Affected);
            var old = generated[0];
            await scheduling.CancelAsync(old.Id, "Holiday", Admin, true, default);
            Assert.Empty((await scheduling.GenerateAsync(cls, range, Admin, default)).Affected);
            Assert.Equal(3, await db.ClassSessions.CountAsync(x => x.ClassScheduleId == schedule.Id));
            Assert.Contains(await scheduling.AuditAsync(old.Id, default), x => x.Action == "Cancelled");
        }
    }

    [Fact]
    public async Task Generation_rejects_entire_batch_if_one_date_conflicts()
    {
        var (db, scheduling, _, cls, teacher, room) = await SetupAsync();
        await using (db)
        {
            var date = new DateOnly(2026, 11, 16);
            await scheduling.CreateAsync(Input(cls, teacher, room), Admin, true, default);
            var recurring = new ScheduleInput(cls, [DayOfWeek.Monday, DayOfWeek.Tuesday],
                new TimeOnly(16, 0), new TimeOnly(17, 0), teacher, SessionMode.Online, Zone.Id,
                date, date.AddDays(1), null);
            Assert.Equal("schedule_conflict", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.CreateScheduleAsync(recurring, Admin, default))).Code);
            Assert.Empty(await db.ClassSchedules.Where(x => x.ClassId == cls).ToListAsync());
            Assert.Empty(await db.ClassSessions.Where(x => x.ClassScheduleId != null).ToListAsync());
        }
    }

    [Fact]
    public async Task Schedule_validates_teacher_timezone_and_class_date_window()
    {
        var (db, scheduling, _, cls, teacher, _) = await SetupAsync();
        await using (db)
        {
            var input = new ScheduleInput(cls, [DayOfWeek.Monday], new TimeOnly(9, 0),
                new TimeOnly(10, 0), teacher, SessionMode.Online, Zone.Id,
                new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 30), null);
            Assert.Equal("invalid_time_zone", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.CreateScheduleAsync(input with { TimeZoneId = "Mars/Unknown" }, Admin, default))).Code);
            Assert.Equal("teacher_not_assigned", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.CreateScheduleAsync(input with { TeacherId = Guid.NewGuid() }, Admin, default))).Code);
            Assert.Equal("outside_class_dates", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.CreateScheduleAsync(input with { EffectiveTo = new DateOnly(2027, 1, 3) }, Admin, default))).Code);
            Assert.Equal("invalid_schedule", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.CreateScheduleAsync(input with { EndTime = input.StartTime }, Admin, default))).Code);
        }
    }

    [Fact]
    public async Task Schedule_update_preserves_time_override_and_reports_terminal_sessions()
    {
        var (db, scheduling, _, cls, teacher, _) = await SetupAsync();
        await using (db)
        {
            var input = new ScheduleInput(cls, [DayOfWeek.Monday], new TimeOnly(9, 0),
                new TimeOnly(10, 0), teacher, SessionMode.Online, Zone.Id,
                new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 30), []);
            var schedule = await scheduling.CreateScheduleAsync(input, Admin, default);
            var sessions = await db.ClassSessions.AsNoTracking().Where(x => x.ClassScheduleId == schedule.Id)
                .OrderBy(x => x.StartAt).ToListAsync();
            Assert.Equal(3, sessions.Count);
            var fixedTime = sessions[0];
            var changed = await scheduling.UpdateAsync(fixedTime.Id, new SessionEdit(teacher, fixedTime.Title,
                null, fixedTime.StartAt.AddMinutes(15), fixedTime.EndAt.AddMinutes(15), SessionMode.Online,
                null, null, "Individual adjustment", fixedTime.Version), Admin, true, default);
            await scheduling.CancelAsync(sessions[1].Id, "Break", Admin, true, default);
            var update = await scheduling.UpdateScheduleAsync(schedule.Id,
                input with { StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(12, 0) },
                schedule.Version, Admin, default);
            Assert.Equal(changed.StartAt, (await scheduling.GetAsync(fixedTime.Id, Admin, "Admin", default)).StartAt);
            Assert.Contains(update.Sessions.Skipped, x => x.SessionId == sessions[1].Id);
            Assert.Equal(SessionStatus.Cancelled,
                (await scheduling.GetAsync(sessions[1].Id, Admin, "Admin", default)).Status);
            Assert.Equal(new TimeOnly(11, 0), update.Schedule.StartTime);
            Assert.Equal(new[] { "Created", "Updated" },
                (await scheduling.ScheduleAuditAsync(schedule.Id, default)).Select(x => x.Action));
            Assert.Equal("concurrency_conflict", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.UpdateScheduleAsync(schedule.Id, input, 0, Admin, default))).Code);
        }
    }

    [Fact]
    public async Task Changing_weekdays_cancels_old_occurrences_including_individual_edits()
    {
        var (db, scheduling, _, cls, teacher, _) = await SetupAsync();
        await using (db)
        {
            var monday = new DateOnly(2026, 11, 16);
            var input = new ScheduleInput(cls, [DayOfWeek.Monday, DayOfWeek.Wednesday],
                new TimeOnly(9, 0), new TimeOnly(10, 0), teacher, SessionMode.Online,
                Zone.Id, monday, monday.AddDays(3), []);
            var schedule = await scheduling.CreateScheduleAsync(input, Admin, default);
            var first = await db.ClassSessions.AsNoTracking().SingleAsync(s => s.OriginalLocalDate == monday);
            await scheduling.UpdateAsync(first.Id, new SessionEdit(teacher, first.Title, null,
                first.StartAt.AddMinutes(15), first.EndAt.AddMinutes(15), SessionMode.Online,
                null, null, "Individual adjustment", first.Version), Admin, true, default);

            await scheduling.UpdateScheduleAsync(schedule.Id,
                input with { DaysOfWeek = [DayOfWeek.Tuesday, DayOfWeek.Thursday] },
                schedule.Version, Admin, default);
            var sessions = await db.ClassSessions.AsNoTracking().OrderBy(s => s.OriginalLocalDate).ToListAsync();
            Assert.Equal(new[] { monday, monday.AddDays(1), monday.AddDays(2), monday.AddDays(3) },
                sessions.Select(s => s.OriginalLocalDate!.Value));
            Assert.Equal(new[] { SessionStatus.Cancelled, SessionStatus.Scheduled,
                SessionStatus.Cancelled, SessionStatus.Scheduled }, sessions.Select(s => s.Status));
        }
    }

    [Fact]
    public async Task Bulk_cancellation_is_audited_and_students_only_see_own_enrolled_classes()
    {
        var (db, scheduling, _, cls, teacher, room) = await SetupAsync();
        await using (db)
        {
            var student = Guid.NewGuid();
            db.ClassEnrollments.Add(new ClassEnrollment { ClassId = cls, StudentId = student,
                Status = EnrollmentStatus.Active });
            await db.SaveChangesAsync();
            var first = await scheduling.CreateAsync(Input(cls, teacher, room), Admin, true, default);
            var second = await scheduling.CreateAsync(Input(cls, teacher, room, 10), Admin, true, default);
            Assert.Equal(room, (await scheduling.GetAsync(first.Id, student, "User", default)).RoomId);
            Assert.Equal("Branch", (await scheduling.GetAsync(first.Id, student, "User", default)).CenterName);
            Assert.Equal(2, (await scheduling.ListAsync(student, "User", null, null, null,
                first.StartAt.AddHours(-1), second.EndAt.AddHours(1), 1, 20, default)).TotalCount);
            Assert.Equal("invalid_cancel_range", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.CancelRangeAsync(cls, first.StartAt, second.EndAt, "", Admin, default))).Code);
            var result = await scheduling.CancelRangeAsync(cls, first.StartAt, second.EndAt,
                "Center closed", Admin, default);
            Assert.Equal(2, result.Affected.Count);
            Assert.All(result.Affected, x => Assert.Equal(SessionStatus.Cancelled, x.Status));
            Assert.Contains(await scheduling.AuditAsync(first.Id, default), x =>
                x.Action == "BulkCancelled" && x.Reason == "Center closed");
            db.ClassEnrollments.Single().Status = EnrollmentStatus.Withdrawn;
            db.ClassEnrollments.Single().WithdrawnAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            Assert.Equal("forbidden_class", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.GetAsync(first.Id, student, "User", default))).Code);
        }
    }

    [Fact]
    public void Scheduling_routes_enforce_role_boundaries()
    {
        Assert.Equal("Admin", typeof(ClassSchedulesController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().Policy);
        var create = typeof(ClassSessionsController).GetMethod("Create")!;
        Assert.Contains(create.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "Admin,Teacher");
        Assert.Equal("Admin", typeof(ClassSessionsController).GetMethod("Audit")!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .First(x => x.Policy is not null).Policy);
    }

    [Fact]
    public async Task Updates_track_overrides_and_terminal_sessions_cannot_change()
    {
        var (db, scheduling, clock, cls, teacher, room) = await SetupAsync();
        await using (db)
        {
            var session = await scheduling.CreateAsync(Input(cls, teacher, room), Admin, true, default);
            var update = new SessionEdit(teacher, "Listening revised", null,
                session.StartAt.AddMinutes(30), session.EndAt.AddMinutes(30),
                SessionMode.Online, null, null, "Room unavailable", session.Version);
            var changed = await scheduling.UpdateAsync(session.Id, update, teacher, false, default);
            Assert.True(changed.TimeOverridden);
            Assert.True(changed.ModeOverridden);
            Assert.Null(changed.RoomId);
            Assert.Equal("concurrency_conflict", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.UpdateAsync(session.Id, update, teacher, false, default))).Code);
            Assert.Equal("session_not_finished", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.CompleteAsync(session.Id, teacher, false, default))).Code);
            clock.Value = new DateTimeOffset(2026, 11, 16, 13, 0, 0, TimeSpan.Zero);
            (await db.Classes.SingleAsync(x => x.Id == cls)).Status = ClassStatus.Completed;
            await db.SaveChangesAsync();
            var completed = await scheduling.CompleteAsync(session.Id, teacher, false, default);
            Assert.Equal(SessionStatus.Completed, completed.Status);
            Assert.Equal("invalid_state", (await Assert.ThrowsAsync<ClassRuleException>(() =>
                scheduling.CancelAsync(session.Id, "Late", teacher, false, default))).Code);
        }
    }

    [Fact]
    public async Task Default_room_propagates_only_eligible_future_offline_sessions_atomically()
    {
        var (db, scheduling, clock, cls, teacher, room) = await SetupAsync();
        await using (db)
        {
            var second = new Room { CenterId = (await db.Rooms.SingleAsync(x => x.Id == room)).CenterId,
                Name = "102", Capacity = 10 };
            db.Rooms.Add(second);
            await db.SaveChangesAsync();
            var first = await scheduling.CreateAsync(Input(cls, teacher, room), Admin, true, default);
            var overridden = await scheduling.CreateAsync(Input(cls, teacher, room, 10), Admin, true, default);
            await scheduling.UpdateAsync(overridden.Id, new SessionEdit(teacher, "Other room", null,
                overridden.StartAt, overridden.EndAt, SessionMode.Offline, second.Id, null,
                "Room preference", overridden.Version), Admin, true, default);
            var handler = new SetDefaultRoomHandler(new FacilityRepository(db), new ClassRepository(db),
                new FakeUnitOfWork(db), clock, NullLogger<SetDefaultRoomHandler>.Instance, scheduling);
            var result = await handler.Handle(new SetDefaultRoomCommand(second.Id, "Change room", true)
                { ClassId = cls, AdminId = Admin }, default);
            Assert.Equal(second.Id, result.Class.DefaultRoomId);
            Assert.Equal(second.Id, (await scheduling.GetAsync(first.Id, Admin, "Admin", default)).RoomId);
            Assert.Equal(second.Id, (await scheduling.GetAsync(overridden.Id, Admin, "Admin", default)).RoomId);
            Assert.True((await scheduling.GetAsync(overridden.Id, Admin, "Admin", default)).RoomOverridden);
            Assert.Contains(await scheduling.AuditAsync(first.Id, default), x => x.Action == "DefaultRoomChanged");
        }
    }

    [Fact]
    public void Date_and_resource_rules_are_deterministic()
    {
        var a = new ClassSession { TeacherId = Guid.NewGuid(), RoomId = Guid.NewGuid(), Mode = SessionMode.Offline,
            StartAt = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc),
            EndAt = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc) };
        var b = new ClassSession { TeacherId = a.TeacherId, Mode = SessionMode.Online,
            StartAt = a.StartAt.AddMinutes(30), EndAt = a.EndAt.AddMinutes(30) };
        Assert.True(SchedulingRules.Conflicts(a, b));
        b.TeacherId = Guid.NewGuid(); b.RoomId = a.RoomId; b.Mode = SessionMode.Offline;
        Assert.True(SchedulingRules.Conflicts(a, b));
        b.RoomId = Guid.NewGuid();
        Assert.False(SchedulingRules.Conflicts(a, b));
        b.RoomId = a.RoomId; b.StartAt = a.EndAt; b.EndAt = b.StartAt.AddHours(1);
        Assert.False(SchedulingRules.Conflicts(a, b));
        Assert.Equal(new DateTime(2026, 11, 16, 2, 0, 0, DateTimeKind.Utc),
            SchedulingRules.LocalToUtc(new DateOnly(2026, 11, 16), new TimeOnly(9, 0), Zone));
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        Assert.Equal("ambiguous_schedule_time", Assert.Throws<ClassRuleException>(() =>
            SchedulingRules.LocalToUtc(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), newYork)).Code);
    }

    [Fact]
    public void Model_enforces_generated_identity_and_optimistic_version()
    {
        using var db = Db();
        var session = db.Model.FindEntityType(typeof(ClassSession))!;
        Assert.True(session.FindProperty(nameof(ClassSession.Version))!.IsConcurrencyToken);
        Assert.Contains(session.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(ClassSession.ClassScheduleId), nameof(ClassSession.OriginalLocalDate)]));
        Assert.NotNull(db.Model.FindEntityType(typeof(SchedulingResource))!.FindPrimaryKey());
    }

    private sealed class FakeUnitOfWork(ClassroomDbContext db) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
        public Task<IClassroomTransaction> BeginTransactionAsync(CancellationToken ct) =>
            Task.FromResult<IClassroomTransaction>(new NoTransaction());
        private sealed class NoTransaction : IClassroomTransaction
        {
            public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
