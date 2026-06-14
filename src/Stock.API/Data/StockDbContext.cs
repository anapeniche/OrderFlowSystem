using Microsoft.EntityFrameworkCore;
using Stock.API.Domain;

namespace Stock.API.Data;

public class StockDbContext : DbContext
{
    public StockDbContext(DbContextOptions<StockDbContext> options) : base(options) { }

    public DbSet<ProductStock> ProductStocks { get; set; }
    public DbSet<ProcessedMessage> ProcessedMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        modelBuilder.Entity<ProductStock>().HasIndex(u => u.ProductId).IsUnique();
        
        modelBuilder.Entity<ProductStock>().HasData(
            new ProductStock { Id = 1, ProductId = 99, Quantity = 10 }
        );
    }
}