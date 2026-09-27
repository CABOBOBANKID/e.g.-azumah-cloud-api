using Microsoft.EntityFrameworkCore;

namespace AzumahCloud.Api.Data;

public sealed class CloudDbContext : DbContext
{
    public CloudDbContext(DbContextOptions<CloudDbContext> options) : base(options) { }
    public DbSet<CloudUser> Users => Set<CloudUser>();
    public DbSet<SyncRecord> SyncRecords => Set<SyncRecord>();
    public DbSet<CloudSale> Sales => Set<CloudSale>();
    public DbSet<CloudPayment> Payments => Set<CloudPayment>();
    public DbSet<CloudExpense> Expenses => Set<CloudExpense>();
    public DbSet<CloudProduct> Products => Set<CloudProduct>();
    public DbSet<CloudCustomer> Customers => Set<CloudCustomer>();
    public DbSet<CloudSupplier> Suppliers => Set<CloudSupplier>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CloudUser>().HasIndex(x => x.Username).IsUnique();
        b.Entity<SyncRecord>().HasIndex(x => x.SyncId).IsUnique();
    }
}
public sealed class CloudUser { public int Id { get; set; } public string Username { get; set; } = ""; public string PasswordHash { get; set; } = ""; public string Role { get; set; } = "Owner"; public bool IsActive { get; set; } }
public sealed class SyncRecord { public int Id { get; set; } public string SyncId { get; set; } = ""; public string EntityType { get; set; } = ""; public int EntityId { get; set; } public string PayloadJson { get; set; } = ""; public DateTime ReceivedAt { get; set; } }
public sealed class CloudSale { public int Id { get; set; } public string SyncId { get; set; } = ""; public string InvoiceNumber { get; set; } = ""; public DateTime CreatedAt { get; set; } public decimal Total { get; set; } public decimal AmountPaid { get; set; } public string PaymentMethod { get; set; } = ""; }
public sealed class CloudPayment { public int Id { get; set; } public string SyncId { get; set; } = ""; public int PartyType { get; set; } public int PartyId { get; set; } public decimal Amount { get; set; } public decimal BalanceAfter { get; set; } public DateTime CreatedAt { get; set; } }
public sealed class CloudExpense { public int Id { get; set; } public string SyncId { get; set; } = ""; public decimal Amount { get; set; } public int Category { get; set; } public string? Note { get; set; } public DateTime CreatedAt { get; set; } }
public sealed class CloudProduct { public int Id { get; set; } public string Name { get; set; } = ""; public decimal StockQuantity { get; set; } public decimal ReorderLevel { get; set; } public string Unit { get; set; } = ""; }
public sealed class CloudCustomer { public int Id { get; set; } public string Name { get; set; } = ""; public string? Phone { get; set; } public decimal Balance { get; set; } }
public sealed class CloudSupplier { public int Id { get; set; } public string Name { get; set; } = ""; public string? Phone { get; set; } public decimal Balance { get; set; } }
