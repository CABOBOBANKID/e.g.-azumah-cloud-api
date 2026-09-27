using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using AzumahCloud.Api.Data;
using AzumahCloud.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CloudDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 5,
                QueueLimit = 0
            }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 120,
                QueueLimit = 0
            }));
});

var jwtKey = builder.Configuration["Jwt:SigningKey"] ?? "";
if (jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization(options => options.AddPolicy("Owner", policy => policy.RequireRole("Owner")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();
    db.Database.EnsureCreated();
    var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();
    var username = builder.Configuration["Owner:Username"] ?? "owner";
    var initialPassword = builder.Configuration["Owner:InitialPassword"] ?? "";
    if (!db.Users.Any())
    {
        if (initialPassword.Length < 8 || initialPassword.StartsWith("CHANGE_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Set a real Owner:InitialPassword (minimum 8 characters) before first API startup.");
        db.Users.Add(new CloudUser { Username = username, PasswordHash = passwordService.Hash(initialPassword), Role = "Owner", IsActive = true });
        db.SaveChanges();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/auth/login", async (LoginRequest request, CloudDbContext db, PasswordService passwords, IConfiguration config) =>
{
    var user = await db.Users.SingleOrDefaultAsync(x => x.Username == request.Username && x.IsActive);
    if (user is null || !passwords.Verify(request.Password, user.PasswordHash))
        return Results.Unauthorized();

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role)
    };
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:SigningKey"]!));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: config["Jwt:Issuer"], audience: config["Jwt:Audience"],
        claims: claims, expires: DateTime.UtcNow.AddHours(8), signingCredentials: creds);
    return Results.Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(token), expiresAt = token.ValidTo });
}).RequireRateLimiting("login");

app.MapGet("/api/dashboard", async (CloudDbContext db) =>
{
    var today = DateTime.UtcNow.Date;
    var tomorrow = today.AddDays(1);
    var salesToday = await db.Sales.Where(x => x.CreatedAt >= today && x.CreatedAt < tomorrow).ToListAsync();
    var recent = await db.Sales.OrderByDescending(x => x.CreatedAt).Take(10).Select(x => new
    {
        x.InvoiceNumber, x.CreatedAt, x.Total, x.AmountPaid,
        Balance = x.Total - x.AmountPaid,
        x.PaymentMethod
    }).ToListAsync();
    var low = (await db.Products.Where(x => x.StockQuantity <= x.ReorderLevel).ToListAsync()).OrderBy(x => x.StockQuantity).Take(20).ToList();
    var customerDebt = (await db.Customers.Where(x => x.Balance > 0).ToListAsync()).OrderByDescending(x => x.Balance).Take(20).ToList();
    var supplierDebt = (await db.Suppliers.Where(x => x.Balance > 0).ToListAsync()).OrderByDescending(x => x.Balance).Take(20).ToList();
    var expenseToday = (await db.Expenses.Where(x => x.CreatedAt >= today && x.CreatedAt < tomorrow).ToListAsync()).Sum(x => x.Amount);

    return Results.Ok(new
    {
        date = today,
        salesToday = salesToday.Sum(x => x.Total),
        cashToday = salesToday.Where(x => x.PaymentMethod == "Cash").Sum(x => x.AmountPaid),
        mobileMoneyToday = salesToday.Where(x => x.PaymentMethod == "MobileMoney").Sum(x => x.AmountPaid),
        creditToday = salesToday.Sum(x => Math.Max(0, x.Total - x.AmountPaid)),
        transactionCount = salesToday.Count,
        customerDebt = customerDebt.Sum(x => x.Balance),
        supplierDebt = supplierDebt.Sum(x => x.Balance),
        expenseToday,
        lowStock = low.Count,
        outOfStock = low.Count(x => x.StockQuantity <= 0),
        recent,
        lowStockItems = low.Select(x => new { x.Name, x.StockQuantity, x.ReorderLevel, x.Unit }),
        customersOwing = customerDebt.Select(x => new { x.Name, x.Phone, x.Balance }),
        suppliersOwing = supplierDebt.Select(x => new { x.Name, x.Phone, x.Balance })
    });
}).RequireAuthorization();

app.MapGet("/api/sales", async (CloudDbContext db, int take = 50) =>
    Results.Ok(await db.Sales.OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync()))
    .RequireAuthorization();

app.MapGet("/api/inventory", async (CloudDbContext db) =>
    Results.Ok(await db.Products.OrderBy(x => x.Name).ToListAsync()))
    .RequireAuthorization();

app.MapGet("/api/customers/debts", async (CloudDbContext db) =>
    Results.Ok((await db.Customers.Where(x => x.Balance > 0).ToListAsync()).OrderByDescending(x => x.Balance).ToList()))
    .RequireAuthorization();

app.MapGet("/api/suppliers/debts", async (CloudDbContext db) =>
    Results.Ok((await db.Suppliers.Where(x => x.Balance > 0).ToListAsync()).OrderByDescending(x => x.Balance).ToList()))
    .RequireAuthorization();

app.MapGet("/api/expenses", async (CloudDbContext db, int take = 50) =>
    Results.Ok(await db.Expenses.OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync()))
    .RequireAuthorization();

app.MapPost("/api/sync/push", async (SyncPushRequest request, CloudDbContext db) =>
{
    foreach (var item in request.Items)
    {
        if (string.IsNullOrWhiteSpace(item.SyncId) || string.IsNullOrWhiteSpace(item.EntityType))
            continue;
        if (await db.SyncRecords.AnyAsync(x => x.SyncId == item.SyncId))
            continue;

        db.SyncRecords.Add(new SyncRecord { SyncId = item.SyncId, EntityType = item.EntityType, EntityId = item.EntityId, PayloadJson = item.PayloadJson, ReceivedAt = DateTime.UtcNow });
        await CloudProjection.ApplyAsync(db, item);
    }
    await db.SaveChangesAsync();
    return Results.Ok(new { accepted = request.Items.Count });
}).RequireAuthorization("Owner");

app.MapPost("/api/sync/bootstrap", async (BootstrapRequest request, CloudDbContext db) =>
{
    await CloudProjection.ReplaceMasterDataAsync(db, request);
    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true });
}).RequireAuthorization("Owner");

app.MapFallbackToFile("index.html");
app.Run();

record LoginRequest(string Username, string Password);
record SyncPushRequest(List<SyncItem> Items);
public record SyncItem(string SyncId, string EntityType, int EntityId, string PayloadJson);
public record BootstrapRequest(List<ProductSnapshot> Products, List<CustomerSnapshot> Customers, List<SupplierSnapshot> Suppliers);
public record ProductSnapshot(int Id, string Name, decimal StockQuantity, decimal ReorderLevel, string Unit);
public record CustomerSnapshot(int Id, string Name, string? Phone, decimal Balance);
public record SupplierSnapshot(int Id, string Name, string? Phone, decimal Balance);