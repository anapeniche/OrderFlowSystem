using Microsoft.EntityFrameworkCore;
using Order.API.Domain.Entities;

namespace Order.API.Data
{
    public class OrderDbContext : DbContext
    {
        public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

        public DbSet<Order.API.Domain.Entities.Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<OutboxEvent> OutboxEvents { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. Mapeia a tabela correta
            modelBuilder.Entity<OrderItem>().ToTable("OrderItemEntity");

            // 2. Mapeia a coluna de preço
            modelBuilder.Entity<OrderItem>()
                .Property(p => p.Price)
                .HasColumnName("UnitPrice");

            // 3. CONFIGURAÇÃO DE CHAVE ESTRANGEIRA REVISADA
            modelBuilder.Entity<Order.API.Domain.Entities.Order>()
                .HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey("OrderEntityId"); 

            // 4. SE A CLASSE OrderItem TIVER UMA PROPRIEDADE CHAMADA OrderId, 
            // PRECISAMOS DIZER AO EF PARA NÃO TENTAR CRIAR UMA COLUNA PARA ELA
            modelBuilder.Entity<OrderItem>().Ignore("OrderId");
        }
    }
}