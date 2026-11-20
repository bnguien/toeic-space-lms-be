using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using ToeicSpace.Payment.Application.DTOs.SePay;
using ToeicSpace.Payment.Domain.Entities;
using ToeicSpace.Payment.Infrastructure;
using ToeicSpace.Payment.Infrastructure.Persistence;
using ToeicSpace.Payment.Infrastructure.Services.SePay;
using Xunit;

namespace ToeicSpace.Payment.Tests;

public sealed class TuitionMySqlFactAttribute : FactAttribute
{
    public TuitionMySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LMS_TUITION_TEST_DB")))
            Skip = "Set LMS_TUITION_TEST_DB to a dedicated lms_test_* database.";
    }
}

public sealed class TuitionMySqlTests
{
    [TuitionMySqlFact]
    public async Task Concurrent_delivery_credits_a_transfer_once()
    {
        var connection = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LMS_TUITION_TEST_DB")
            ?? throw new InvalidOperationException("Missing LMS tuition test database."));
        Assert.StartsWith("lms_test_", connection.Database);
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseMySql(connection.ConnectionString, new MySqlServerVersion(new Version(8, 4, 0))).Options;
        await using var db = new PaymentDbContext(options);
        await db.Database.MigrateAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["SePay:AccountNumber"] = "test-account" }).Build();
        var service = new TuitionOrders(db, config);
        var order = await service.Create(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 100), default);
        var at = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));
        var transfer = new SePayWebhook(Random.Shared.NextInt64(1, long.MaxValue), null,
            at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), "test-account",
            order.PaymentReference, null, "in", 30, null);
        async Task Deliver()
        {
            await using var scope = new PaymentDbContext(options);
            await new SePayWebhookProcessor(scope, config).ProcessAsync(transfer, default);
        }
        await Task.WhenAll(Deliver(), Deliver());
        var result = await service.Get(new(order.PaymentOrderId, order.StudentId), default);
        Assert.Single(result.Entries);
        Assert.Equal(30, result.Order.AmountPaid);
        Assert.Equal(70, result.Order.RemainingBalance);
    }
}
