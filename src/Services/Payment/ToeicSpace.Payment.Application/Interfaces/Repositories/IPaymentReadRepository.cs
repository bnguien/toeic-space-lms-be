using ToeicSpace.Payment.Application.DTOs.Reconciliation;
using ToeicSpace.Payment.Domain.Entities;

namespace ToeicSpace.Payment.Application.Interfaces.Repositories;

public interface IPaymentReadRepository
{
    Task<ReconciliationPage<PaymentTransaction>> GetExceptionsAsync(int page, int pageSize, CancellationToken ct);
}
