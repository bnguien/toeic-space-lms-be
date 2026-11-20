using Microsoft.EntityFrameworkCore;
using ToeicSpace.Payment.Application.DTOs.Reconciliation;
using ToeicSpace.Payment.Application.Interfaces.Repositories;
using ToeicSpace.Payment.Domain.Entities;
using ToeicSpace.Payment.Domain.Enums;

namespace ToeicSpace.Payment.Infrastructure.Persistence.Repositories;

public sealed class PaymentReadRepository(PaymentDbContext db) : IPaymentReadRepository
{
    public async Task<ReconciliationPage<PaymentTransaction>> GetExceptionsAsync(int page, int pageSize, CancellationToken ct)
    {
        var query = db.Transactions.AsNoTracking().Where(x => x.Status != TransactionStatus.Matched);
        return new ReconciliationPage<PaymentTransaction>(await query.CountAsync(ct),
            await query.OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.SePayId)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct));
    }

}
