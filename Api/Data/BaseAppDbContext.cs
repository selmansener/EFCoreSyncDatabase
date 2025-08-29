using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public abstract class BaseAppDbContext : DbContext
{
    protected BaseAppDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLineItem> OrderLineItems => Set<OrderLineItem>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Email).IsRequired().HasMaxLength(200);
            entity.HasIndex(p => p.Email).IsUnique();

            entity.HasMany(e => e.Addresses)
                  .WithOne(a => a.Customer)
                  .HasForeignKey(a => a.CustomerId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Orders)
                  .WithOne(o => o.Customer)
                  .HasForeignKey(o => o.CustomerId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Address>(entity =>
        {
            entity.Property(p => p.Street).IsRequired().HasMaxLength(200);
            entity.Property(p => p.City).IsRequired().HasMaxLength(100);
            entity.Property(p => p.State).IsRequired().HasMaxLength(100);
            entity.Property(p => p.PostalCode).IsRequired().HasMaxLength(20);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Sku).IsRequired().HasMaxLength(50);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Price).HasColumnType("decimal(18,2)");
            entity.HasIndex(p => p.Sku).IsUnique();
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(p => p.OrderDate).IsRequired();
        });

        modelBuilder.Entity<OrderLineItem>(entity =>
        {
            entity.Property(p => p.UnitPrice).HasColumnType("decimal(18,2)");
            entity.HasOne(li => li.Order)
                  .WithMany(o => o.LineItems)
                  .HasForeignKey(li => li.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(li => li.Product)
                  .WithMany(p => p.LineItems)
                  .HasForeignKey(li => li.ProductId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

