using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToeicSpace.BuildingBlocks.Security.Jwt;
using ToeicSpace.Payment.Application.Interfaces.Services;
using ToeicSpace.Payment.Domain.Entities;
using ToeicSpace.Payment.Domain.Exceptions;

namespace ToeicSpace.Payment.API.Controllers;

[ApiController]
[Route("api/v1/tuition-orders")]
[Authorize]
public sealed class OrdersController(ISender sender) : ControllerBase
{
    private Guid Subject => Guid.TryParse(User.FindFirst(AccessTokenDefaults.SubjectClaim)?.Value, out var id)
        ? id : throw new PaymentException("invalid_subject", "Invalid user identity.", 401);

    [HttpPost, Authorize(Roles = "Admin")]
    public Task<TuitionOrder> Create(CreateTuitionOrder request, CancellationToken ct) => sender.Send(request, ct);

    [HttpGet("{id:guid}")]
    public Task<TuitionStatement> Get(Guid id, CancellationToken ct) =>
        sender.Send(new GetTuitionOrder(id, User.IsInRole("Admin") ? null : Subject), ct);

    public sealed record EntryRequest(Guid EntryId, TuitionEntryKind Kind, decimal Amount);

    [HttpPost("{id:guid}/entries"), Authorize(Roles = "Admin")]
    public Task<TuitionOrder> Record(Guid id, EntryRequest request, CancellationToken ct) =>
        sender.Send(new RecordTuitionEntry(id, request.EntryId, request.Kind, request.Amount, Subject), ct);
}
