using ToeicSpace.Payment.Domain.Exceptions;

namespace ToeicSpace.Payment.Domain.Entities;

public enum TuitionEntryKind { SePay, Cash, Waiver }

public sealed class TuitionOrder
{
    public Guid PaymentOrderId { get; init; }
    public Guid ClassId { get; init; }
    public Guid StudentId { get; init; }
    public Guid? ClassEnrollmentId { get; init; }
    public decimal AmountOwed { get; init; }
    public decimal AmountPaid { get; private set; }
    public decimal AmountWaived { get; private set; }
    public decimal RemainingBalance => AmountOwed - AmountPaid - AmountWaived;
    public required string PaymentReference { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public void Apply(TuitionEntryKind kind, decimal amount)
    {
        if (amount <= 0 || amount != decimal.Truncate(amount) || amount > RemainingBalance)
            throw new PaymentException("invalid_amount", "Amount must be whole VND within the outstanding balance.");
        if (kind == TuitionEntryKind.Waiver) AmountWaived += amount;
        else AmountPaid += amount;
    }
}

public sealed class TuitionEntry
{
    public Guid Id { get; init; }
    public Guid PaymentOrderId { get; init; }
    public TuitionEntryKind Kind { get; init; }
    public decimal Amount { get; init; }
    public Guid? RecordedBy { get; init; }
    public long? SePayId { get; init; }
    public DateTime RecordedAt { get; init; } = DateTime.UtcNow;
}
