using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AzumahCloud.Api.Data;
namespace AzumahCloud.Api.Services;
public static class CloudProjection
{
    public static async Task ApplyAsync(CloudDbContext db, SyncItem item)
    {
        using var doc = JsonDocument.Parse(item.PayloadJson);
        var root = doc.RootElement;
        switch (item.EntityType)
        {
            case "Sale":
                if (!await db.Sales.AnyAsync(x => x.SyncId == item.SyncId))
                    db.Sales.Add(new CloudSale { SyncId = item.SyncId, Id = root.GetProperty("Id").GetInt32(), InvoiceNumber = root.GetProperty("InvoiceNumber").GetString() ?? "", CreatedAt = root.GetProperty("CreatedAt").GetDateTime(), Total = root.GetProperty("Total").GetDecimal(), AmountPaid = root.GetProperty("AmountPaid").GetDecimal(), PaymentMethod = root.GetProperty("PaymentMethod").GetString() ?? "" });
                break;
            case "Payment":
                if (!await db.Payments.AnyAsync(x => x.SyncId == item.SyncId))
                    db.Payments.Add(new CloudPayment { SyncId = item.SyncId, Id = root.GetProperty("Id").GetInt32(), PartyType = root.GetProperty("PartyType").GetInt32(), PartyId = root.GetProperty("PartyId").GetInt32(), Amount = root.GetProperty("Amount").GetDecimal(), BalanceAfter = root.GetProperty("BalanceAfter").GetDecimal(), CreatedAt = root.GetProperty("CreatedAt").GetDateTime() });
                break;
            case "Expense":
                if (!await db.Expenses.AnyAsync(x => x.SyncId == item.SyncId))
                    db.Expenses.Add(new CloudExpense { SyncId = item.SyncId, Id = root.GetProperty("Id").GetInt32(), Amount = root.GetProperty("Amount").GetDecimal(), Category = root.GetProperty("Category").GetInt32(), Note = root.TryGetProperty("Note", out var note) ? note.GetString() : null, CreatedAt = root.GetProperty("CreatedAt").GetDateTime() });
                break;
        }
    }
    public static async Task ReplaceMasterDataAsync(CloudDbContext db, BootstrapRequest request)
    {
        await db.Products.ExecuteDeleteAsync(); await db.Customers.ExecuteDeleteAsync(); await db.Suppliers.ExecuteDeleteAsync();
        foreach (var p in request.Products) db.Products.Add(new CloudProduct { Id = p.Id, Name = p.Name, StockQuantity = p.StockQuantity, ReorderLevel = p.ReorderLevel, Unit = p.Unit });
        foreach (var c in request.Customers) db.Customers.Add(new CloudCustomer { Id = c.Id, Name = c.Name, Phone = c.Phone, Balance = c.Balance });
        foreach (var s in request.Suppliers) db.Suppliers.Add(new CloudSupplier { Id = s.Id, Name = s.Name, Phone = s.Phone, Balance = s.Balance });
    }
}
