using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using ToeicSpace.Classroom.API.Controllers;
using ToeicSpace.Classroom.Application.Features.Centers.Commands.CreateCenter;
using ToeicSpace.Classroom.Application.Features.Centers.Commands.UpdateCenter;
using ToeicSpace.Classroom.Application.Features.Centers.Commands.SetCenterStatus;
using ToeicSpace.Classroom.Application.Features.Centers.Queries.ListCenters;
using ToeicSpace.Classroom.Application.Features.Rooms.Commands.CreateRoom;
using ToeicSpace.Classroom.Application.Features.Rooms.Commands.UpdateRoom;
using ToeicSpace.Classroom.Application.Features.Rooms.Commands.SetRoomStatus;
using ToeicSpace.Classroom.Application.Features.Rooms.Queries.ListRooms;
using ToeicSpace.Classroom.Application.Features.Classes.Commands.SetDefaultRoom;
using ToeicSpace.Classroom.Application.Interfaces;
using ToeicSpace.Classroom.Domain.Entities;
using ToeicSpace.Classroom.Domain.Enums;
using ToeicSpace.Classroom.Domain.Exceptions;
using ToeicSpace.Classroom.Infrastructure.Persistence;
using ToeicSpace.Classroom.Infrastructure.Persistence.Repositories;
using Xunit;

namespace ToeicSpace.Classroom.Tests;

public sealed class FacilityManagementTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private static ClassroomDbContext Db() => new(new DbContextOptionsBuilder<ClassroomDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static (FacilityRepository repo, IUnitOfWork uow) Setup(ClassroomDbContext db) =>
        (new FacilityRepository(db), new TestUnitOfWork(db));

    [Fact]
    public async Task Center_crud_search_and_activation_preserve_rooms()
    {
        await using var db = Db();
        var (repo, uow) = Setup(db);
        var create = new CreateCenterHandler(repo, uow, TimeProvider.System);
        var first = await create.Handle(new(" Chi nhánh A ", " 123 Main St "), default);
        Assert.Equal("Asia/Ho_Chi_Minh", first.TimeZoneId);
        Assert.Equal("Chi nhánh A", first.Name);
        Assert.Equal("123 Main St", first.Address);
        Assert.Equal(1, (await new ListCentersHandler(repo).Handle(new("Chi nhánh", null), default)).TotalCount);
        var updated = await new UpdateCenterHandler(repo, uow, TimeProvider.System)
            .Handle(new(first.Id, "Branch B", "Other St", "UTC"), default);
        Assert.Equal("UTC", updated.TimeZoneId);
        var room = await new CreateRoomHandler(repo, uow, TimeProvider.System)
            .Handle(new(first.Id, "101", 20), default);
        var inactive = await new SetCenterStatusHandler(repo, uow, TimeProvider.System)
            .Handle(new(first.Id, FacilityStatus.Inactive), default);
        Assert.Equal(FacilityStatus.Inactive, inactive.Status);
        Assert.NotNull(await repo.RoomAsync(room.Id, default));
        Assert.Equal("inactive_center", (await Assert.ThrowsAsync<ClassRuleException>(() =>
            new CreateRoomHandler(repo, uow, TimeProvider.System).Handle(new(first.Id, "102", 10), default))).Code);
        Assert.Equal(FacilityStatus.Active, (await new SetCenterStatusHandler(repo, uow, TimeProvider.System)
            .Handle(new(first.Id, FacilityStatus.Active), default)).Status);
        Assert.Equal("invalid_time_zone", (await Assert.ThrowsAsync<ClassRuleException>(() =>
            create.Handle(new("Branch", "Address", "Mars/Unknown"), default))).Code);
    }

    [Fact]
    public async Task Room_validation_uniqueness_and_search()
    {
        await using var db = Db();
        var (repo, uow) = Setup(db);
        var createCenter = new CreateCenterHandler(repo, uow, TimeProvider.System);
        var a = await createCenter.Handle(new("A", "A address"), default);
        var b = await createCenter.Handle(new("B", "B address"), default);
        var create = new CreateRoomHandler(repo, uow, TimeProvider.System);
        var room = await create.Handle(new(a.Id, "R1", 12), default);
        Assert.Equal("duplicate_room", (await Assert.ThrowsAsync<ClassRuleException>(() =>
            create.Handle(new(a.Id, "R1", 12), default))).Code);
        Assert.Equal("invalid_room", (await Assert.ThrowsAsync<ClassRuleException>(() =>
            create.Handle(new(a.Id, "R2", 0), default))).Code);
        await create.Handle(new(b.Id, "R1", 15), default);
        var updated = await new UpdateRoomHandler(repo, uow, TimeProvider.System)
            .Handle(new(room.Id, a.Id, "R2", 22), default);
        Assert.Equal(22, updated.Capacity);
        await create.Handle(new(a.Id, "R3", 18), default);
        Assert.Equal("duplicate_room", (await Assert.ThrowsAsync<ClassRuleException>(() =>
            new UpdateRoomHandler(repo, uow, TimeProvider.System)
                .Handle(new(room.Id, a.Id, "R3", 22), default))).Code);
        Assert.Equal("immutable_center", (await Assert.ThrowsAsync<ClassRuleException>(() =>
            new UpdateRoomHandler(repo, uow, TimeProvider.System).Handle(new(room.Id, b.Id, "R2", 22), default))).Code);
        Assert.Single((await new ListRoomsHandler(repo).Handle(new(a.Id, "R2", null), default)).Items);
        Assert.Equal("invalid_filter", (await Assert.ThrowsAsync<ClassRuleException>(() =>
            new ListRoomsHandler(repo).Handle(new(null, null, null, 0), default))).Code);
    }

    [Fact]
    public async Task Assignment_checks_capacity_active_resources_reason_and_audits_changes()
    {
        await using var db = Db();
        var (repo, uow) = Setup(db);
        var center = await new CreateCenterHandler(repo, uow, TimeProvider.System)
            .Handle(new("A", "Address"), default);
        var makeRoom = new CreateRoomHandler(repo, uow, TimeProvider.System);
        var small = await makeRoom.Handle(new(center.Id, "Small", 1), default);
        var large = await makeRoom.Handle(new(center.Id, "Large", 2), default);
        var classId = Guid.NewGuid();
        db.Classes.Add(new Class { Id = classId, Code = "ROOM-" + classId.ToString("N"), Name = "Test class",
            Status = ClassStatus.Active, Description = "Keep", Version = 7 });
        db.ClassEnrollments.Add(new ClassEnrollment { ClassId = classId, StudentId = Guid.NewGuid(), Status = EnrollmentStatus.Active });
        db.ClassEnrollments.Add(new ClassEnrollment { ClassId = classId, StudentId = Guid.NewGuid(), Status = EnrollmentStatus.Active });
        db.ClassEnrollments.Add(new ClassEnrollment { ClassId = classId, StudentId = Guid.NewGuid(), Status = EnrollmentStatus.Withdrawn,
            WithdrawnAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var handler = new SetDefaultRoomHandler(repo, new ClassRepository(db), uow, TimeProvider.System,
            NullLogger<SetDefaultRoomHandler>.Instance);
        async Task<ToeicSpace.Classroom.Application.DTOs.Classes.ClassDetailDto> Set(Guid? room, string? reason = null) =>
            await handler.Handle(new SetDefaultRoomCommand(room, reason) { ClassId = classId, AdminId = Admin }, default);
        Assert.Equal("room_too_small", (await Assert.ThrowsAsync<ClassRuleException>(() => Set(small.Id))).Code);
        Assert.Null((await db.Classes.SingleAsync(x => x.Id == classId)).DefaultRoomId);
        var first = await Set(large.Id);
        Assert.Equal(large.Id, first.Class.DefaultRoomId);
        Assert.Equal("Keep", first.Class.Description);
        Assert.Equal(8, (await db.Classes.SingleAsync(x => x.Id == classId)).Version);
        Assert.Equal("room_too_small", (await Assert.ThrowsAsync<ClassRuleException>(() =>
            new UpdateRoomHandler(repo, uow, TimeProvider.System)
                .Handle(new(large.Id, center.Id, "Large", 1), default))).Code);
        var secondClassId = Guid.NewGuid();
        db.Classes.Add(new Class { Id = secondClassId, Code = "SECOND-" + secondClassId.ToString("N"),
            Name = "Other class", Status = ClassStatus.Draft });
        await db.SaveChangesAsync();
        Assert.Equal(large.Id, (await handler.Handle(new SetDefaultRoomCommand(large.Id, null)
            { ClassId = secondClassId, AdminId = Admin }, default)).Class.DefaultRoomId);
        Assert.Equal(2, await db.Classes.CountAsync(x => x.DefaultRoomId == large.Id));
        Assert.Equal("room_change_reason_required", (await Assert.ThrowsAsync<ClassRuleException>(() => Set(null))).Code);
        Assert.Equal(large.Id, (await db.Classes.SingleAsync(x => x.Id == classId)).DefaultRoomId);
        await new SetRoomStatusHandler(repo, uow, TimeProvider.System)
            .Handle(new(small.Id, FacilityStatus.Inactive), default);
        Assert.Equal("inactive_room", (await Assert.ThrowsAsync<ClassRuleException>(() => Set(small.Id, "Move"))).Code);
        await new SetCenterStatusHandler(repo, uow, TimeProvider.System)
            .Handle(new(center.Id, FacilityStatus.Inactive), default);
        await new SetRoomStatusHandler(repo, uow, TimeProvider.System)
            .Handle(new(small.Id, FacilityStatus.Active), default);
        Assert.Equal("inactive_room", (await Assert.ThrowsAsync<ClassRuleException>(() => Set(small.Id, "Move"))).Code);
        Assert.Null((await Set(null, "Lớp chuyển sang online")).Class.DefaultRoomId);
        var history = await repo.RoomHistoryAsync(classId, default);
        Assert.Equal(2, history.Count);
        Assert.Equal(large.Id, history[0].NewRoomId);
        Assert.Equal(large.Id, history[1].PreviousRoomId);
        Assert.Equal(Admin, history[1].ChangedBy);
        Assert.Equal("Lớp chuyển sang online", history[1].Reason);
        (await db.Classes.SingleAsync(x => x.Id == classId)).Status = ClassStatus.Completed;
        await db.SaveChangesAsync();
        Assert.Equal("invalid_state", (await Assert.ThrowsAsync<ClassRuleException>(() =>
            handler.Handle(new SetDefaultRoomCommand(large.Id, null) { ClassId = classId, AdminId = Admin }, default))).Code);
    }

    [Fact]
    public void Management_routes_require_admin()
    {
        foreach (var controller in new[] { typeof(CentersController), typeof(RoomsController) })
            Assert.Equal("Admin", controller.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        foreach (var name in new[] { "SetDefaultRoom", "DefaultRoomHistory" })
            Assert.Contains(typeof(ClassesController).GetMethod(name)!.GetCustomAttributes<AuthorizeAttribute>(),
                attribute => attribute.Policy == "Admin");
    }

    [Fact]
    public void Facility_model_enforces_unique_room_name_per_center_and_optional_default_room()
    {
        using var db = Db();
        var room = db.Model.FindEntityType(typeof(Room))!;
        Assert.Contains(room.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual([nameof(Room.CenterId), nameof(Room.Name)]));
        Assert.Contains(room.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Center) &&
            fk.DeleteBehavior == DeleteBehavior.Restrict);
        var entity = db.Model.FindEntityType(typeof(Class))!;
        Assert.True(entity.FindProperty(nameof(Class.DefaultRoomId))!.IsNullable);
        Assert.Contains(entity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Room) &&
            fk.DeleteBehavior == DeleteBehavior.Restrict);
    }

    private sealed class TestUnitOfWork(ClassroomDbContext db) : IUnitOfWork
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
