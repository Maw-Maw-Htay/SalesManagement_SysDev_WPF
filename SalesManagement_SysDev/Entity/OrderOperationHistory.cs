using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed class OrderOperationHistory
{
    public long Id { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string Operation { get; set; } = "";
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public int OfficeId { get; set; }
    public int? BeforeState { get; set; }
    public int? AfterState { get; set; }
    public int? BeforeManagement { get; set; }
    public int? AfterManagement { get; set; }
    public string Details { get; set; } = "";
    public int? ActorEmployeeId { get; set; }
    public string ActorName { get; set; } = "";
    public string ActorOfficeName { get; set; } = "";
}

public partial class SalesManagementContext
{
    public DbSet<OrderOperationHistory> OrderOperationHistories => Set<OrderOperationHistory>();

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderOperationHistory>(entity =>
        {
            entity.ToTable("T_OrderOperationHistory");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RecordedAt).HasColumnType("datetimeoffset");
            entity.Property(x => x.Operation).HasMaxLength(40);
            entity.Property(x => x.CustomerName).HasMaxLength(100);
            entity.Property(x => x.ActorName).HasMaxLength(100);
            entity.Property(x => x.ActorOfficeName).HasMaxLength(100);
            entity.HasIndex(x => new { x.OfficeId, x.RecordedAt });
            entity.HasIndex(x => x.OrderId);
            // No foreign key: retain the recorded facts when an order or master record changes.
        });
    }
}
