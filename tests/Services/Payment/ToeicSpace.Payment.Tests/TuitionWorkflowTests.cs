using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using ToeicSpace.Payment.Application.DTOs.SePay;
using ToeicSpace.Payment.Application.Interfaces.Services;
using ToeicSpace.Payment.Domain.Entities;
using ToeicSpace.Payment.Domain.Enums;
using ToeicSpace.Payment.Infrastructure;
using ToeicSpace.Payment.Infrastructure.Persistence;
using ToeicSpace.Payment.Infrastructure.Services.SePay;
using Xunit;

namespace ToeicSpace.Payment.Tests;

public sealed class TuitionWorkflowTests
{
    // Each test owns its database. SQLite serializes writers at database level.
    // This adapter exercises balances/replay without claiming MySQL row-lock coverage.
    private sealed class SqliteLockSyntax : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            command.CommandText = command.CommandText.Replace(" FOR UPDATE", "");
            return base.ReaderExecutingAsync(command, eventData, result, ct);
        }
    }

    [Fact]
    public async Task Verified_installment_replays_once_and_manual_entries_settle_the_balance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseSqlite(connection).AddInterceptors(new SqliteLockSyntax()).Options);
        await db.Database.EnsureCreatedAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["SePay:AccountNumber"] = "test-account" }).Build();
        var service = new TuitionOrders(db, config);
        var create = new CreateTuitionOrder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 100);
        var order = await service.Create(create, default);
        var processor = new SePayWebhookProcessor(db, config);
        var at = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));
        var transfer = new SePayWebhook(1, null, at.ToString("yyyy-MM-dd HH:mm:ss"), "test-account",
            order.PaymentReference, null, "in", 30, null);
        await processor.ProcessAsync(transfer with { Id = 2, AccountNumber = "wrong" }, default);
        await processor.ProcessAsync(transfer with { Id = 3, TransferAmount = 101 }, default);
        await processor.ProcessAsync(transfer, default);
        await processor.ProcessAsync(transfer, default);
        var admin = Guid.NewGuid();
        var cash = new RecordTuitionEntry(order.PaymentOrderId, Guid.NewGuid(), TuitionEntryKind.Cash, 60, admin);
        await service.Record(cash, default);
        await service.Record(cash, default);
        await service.Record(cash with { EntryId = Guid.NewGuid(), Kind = TuitionEntryKind.Waiver, Amount = 10 }, default);
        db.ChangeTracker.Clear();
        var statement = await service.Get(new(order.PaymentOrderId, create.StudentId), default);
        Assert.Equal(90, statement.Order.AmountPaid);
        Assert.Equal(10, statement.Order.AmountWaived);
        Assert.Equal(0, statement.Order.RemainingBalance);
        Assert.Equal(3, statement.Entries.Count);
        Assert.Equal(TransactionStatus.Invalid, (await db.Transactions.SingleAsync(x => x.SePayId == 2)).Status);
        Assert.Equal(TransactionStatus.Overpaid, (await db.Transactions.SingleAsync(x => x.SePayId == 3)).Status);
        Assert.Single(await db.Transactions.Where(x => x.SePayId == 1).ToListAsync());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.Get(new(order.PaymentOrderId, Guid.NewGuid()), default));
    }
}
