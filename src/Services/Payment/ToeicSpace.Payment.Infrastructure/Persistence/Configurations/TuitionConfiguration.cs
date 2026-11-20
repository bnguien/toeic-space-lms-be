using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToeicSpace.Payment.Domain.Entities;

namespace ToeicSpace.Payment.Infrastructure.Persistence.Configurations;

public sealed class TuitionConfiguration : IEntityTypeConfiguration<TuitionOrder>, IEntityTypeConfiguration<TuitionEntry>
{
    public void Configure(EntityTypeBuilder<TuitionOrder> b)
    {
        b.ToTable("TuitionOrders");
        b.HasKey(x => x.PaymentOrderId);
        b.Property(x => x.PaymentReference).HasMaxLength(20);
        b.HasIndex(x => x.PaymentReference).IsUnique();
        b.HasIndex(x => new { x.ClassId, x.StudentId });
        b.Property(x => x.AmountOwed).HasPrecision(18, 0);
        b.Property(x => x.AmountPaid).HasPrecision(18, 0);
        b.Property(x => x.AmountWaived).HasPrecision(18, 0);
    }

    public void Configure(EntityTypeBuilder<TuitionEntry> b)
    {
        b.ToTable("TuitionEntries");
        b.HasKey(x => x.Id);
        b.HasOne<TuitionOrder>().WithMany().HasForeignKey(x => x.PaymentOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.SePayId).IsUnique();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.Amount).HasPrecision(18, 0);
    }
}
