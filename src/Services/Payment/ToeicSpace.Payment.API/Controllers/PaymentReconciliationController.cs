using MediatR;
using ToeicSpace.Payment.Application.Features.Reconciliation.Queries.GetTransactionExceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ToeicSpace.Payment.API.Controllers;

[ApiController]
[Route("api/v1/admin/payment-reconciliation")]
[Authorize(Roles = "Admin")]
public sealed class PaymentReconciliationController(ISender sender) : ControllerBase
{
    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions([FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        return Ok(await sender.Send(new GetTransactionExceptionsQuery(page, pageSize), ct));
    }

}
