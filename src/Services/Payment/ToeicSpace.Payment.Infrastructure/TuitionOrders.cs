using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using ToeicSpace.Payment.Application.Interfaces.Services;
using ToeicSpace.Payment.Domain.Entities;
using ToeicSpace.Payment.Domain.Exceptions;
using ToeicSpace.Payment.Infrastructure.Persistence;

namespace ToeicSpace.Payment.Infrastructure;

public sealed class TuitionOrders(PaymentDbContext db, IConfiguration config) : ITuitionOrders
{
    public async Task<TuitionOrder> Create(CreateTuitionOrder r, CancellationToken ct)
    {
        if (r.PaymentOrderId == Guid.Empty || r.ClassId == Guid.Empty || r.StudentId == Guid.Empty ||
            r.ClassEnrollmentId == Guid.Empty || r.AmountOwed <= 0 || r.AmountOwed > 999999999999999999m ||
            decimal.Truncate(r.AmountOwed) != r.AmountOwed)
            throw new PaymentException("invalid_order", "Supply valid identifiers and a positive whole VND amount.", 400);
        var order = new TuitionOrder { PaymentOrderId = r.PaymentOrderId, StudentId = r.StudentId,
            ClassId = r.ClassId, ClassEnrollmentId = r.ClassEnrollmentId, AmountOwed = r.AmountOwed,
            PaymentReference = "TL" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)) };
        db.Add(order);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is MySqlException { Number: 1062 })
        {
            db.ChangeTracker.Clear();
            var existing = await db.Set<TuitionOrder>().FindAsync([r.PaymentOrderId], ct);
            if (existing == null || existing.ClassId != r.ClassId || existing.StudentId != r.StudentId ||
                existing.ClassEnrollmentId != r.ClassEnrollmentId || existing.AmountOwed != r.AmountOwed)
                throw new PaymentException("order_conflict", "Order identifiers conflict with an existing order.");
            return existing;
        }
        return order;
    }

    public async Task<TuitionOrder> Record(RecordTuitionEntry r, CancellationToken ct)
    {
        if (r.EntryId == Guid.Empty || r.Kind is not (TuitionEntryKind.Cash or TuitionEntryKind.Waiver))
            throw new PaymentException("invalid_entry", "Supply a unique entry ID and Cash or Waiver.", 400);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Set<TuitionOrder>().FromSqlInterpolated(
            $"SELECT * FROM TuitionOrders WHERE PaymentOrderId = {r.OrderId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException();
        var existing = await db.Set<TuitionEntry>().FindAsync([r.EntryId], ct);
        if (existing != null)
        {
            if (existing.PaymentOrderId != r.OrderId || existing.Kind != r.Kind || existing.Amount != r.Amount ||
                existing.RecordedBy != r.AdminId) throw new PaymentException("entry_conflict", "Entry ID already used.");
            return order;
        }
        order.Apply(r.Kind, r.Amount);
        db.Add(new TuitionEntry { Id = r.EntryId, PaymentOrderId = r.OrderId, Kind = r.Kind,
            Amount = r.Amount, RecordedBy = r.AdminId });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return order;
    }

    public async Task<TuitionStatement> Get(GetTuitionOrder r, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var order = await db.Set<TuitionOrder>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.PaymentOrderId == r.OrderId && (r.StudentId == null || x.StudentId == r.StudentId), ct)
            ?? throw new KeyNotFoundException();
        var entries = await db.Set<TuitionEntry>().AsNoTracking().Where(x => x.PaymentOrderId == r.OrderId)
            .OrderBy(x => x.RecordedAt).ThenBy(x => x.Id).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return new(order, entries, config["SePay:Bank"], config["SePay:AccountNumber"]);
    }
}
