using Microsoft.EntityFrameworkCore;

namespace Stove.Net.Tests.ExampleApp;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.ProductName).HasColumnName("product_name").IsRequired();
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("Pending");
        });
    }
}
