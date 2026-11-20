using ToeicSpace.Payment.Domain.Entities;
using ToeicSpace.Payment.Domain.Exceptions;
using Xunit;

namespace ToeicSpace.Payment.Tests;

public sealed class TuitionTests
{
    [Fact]
    public void Installments_and_waivers_settle_without_an_enrollment()
    {
        var order = new TuitionOrder { PaymentReference = "test", AmountOwed = 100 };
        order.Apply(TuitionEntryKind.SePay, 20);
        order.Apply(TuitionEntryKind.Cash, 50);
        Assert.Equal(30, order.RemainingBalance);
        order.Apply(TuitionEntryKind.Waiver, 30);
        Assert.Equal(70, order.AmountPaid);
        Assert.Equal(30, order.AmountWaived);
        Assert.Equal(0, order.RemainingBalance);
        Assert.Null(order.ClassEnrollmentId);
        Assert.Throws<PaymentException>(() => order.Apply(TuitionEntryKind.Cash, 1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(101)]
    public void Invalid_entry_preserves_balance(decimal amount)
    {
        var order = new TuitionOrder { PaymentReference = "test", AmountOwed = 100 };
        Assert.Throws<PaymentException>(() => order.Apply(TuitionEntryKind.Waiver, amount));
        Assert.Equal(100, order.RemainingBalance);
    }
}
