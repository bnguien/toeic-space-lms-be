using MediatR;
using ToeicSpace.Payment.Domain.Entities;

namespace ToeicSpace.Payment.Application.Interfaces.Services;

public sealed record CreateTuitionOrder(Guid PaymentOrderId, Guid ClassId, Guid StudentId,
    Guid? ClassEnrollmentId, decimal AmountOwed) : IRequest<TuitionOrder>;
public sealed record RecordTuitionEntry(Guid OrderId, Guid EntryId, TuitionEntryKind Kind,
    decimal Amount, Guid AdminId) : IRequest<TuitionOrder>;
public sealed record GetTuitionOrder(Guid OrderId, Guid? StudentId) : IRequest<TuitionStatement>;
public sealed record TuitionStatement(TuitionOrder Order, IReadOnlyList<TuitionEntry> Entries,
    string? Bank, string? AccountNumber);

public interface ITuitionOrders
{
    Task<TuitionOrder> Create(CreateTuitionOrder request, CancellationToken ct);
    Task<TuitionOrder> Record(RecordTuitionEntry request, CancellationToken ct);
    Task<TuitionStatement> Get(GetTuitionOrder request, CancellationToken ct);
}

public sealed class TuitionHandlers(ITuitionOrders orders) : IRequestHandler<CreateTuitionOrder, TuitionOrder>,
    IRequestHandler<RecordTuitionEntry, TuitionOrder>, IRequestHandler<GetTuitionOrder, TuitionStatement>
{
    public Task<TuitionOrder> Handle(CreateTuitionOrder r, CancellationToken ct) => orders.Create(r, ct);
    public Task<TuitionOrder> Handle(RecordTuitionEntry r, CancellationToken ct) => orders.Record(r, ct);
    public Task<TuitionStatement> Handle(GetTuitionOrder r, CancellationToken ct) => orders.Get(r, ct);
}
