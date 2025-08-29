using Api.Data;
using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public static class SeedService
{
    public static async Task<object> ResetAndSeedAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        using var scope = sp.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<SourceDbContext>();
        var target = scope.ServiceProvider.GetRequiredService<TargetDbContext>();
        var mappings = scope.ServiceProvider.GetRequiredService<EntityMappingsDbContext>();

        // Reset databases
        await mappings.Database.EnsureDeletedAsync(ct);
        await source.Database.EnsureDeletedAsync(ct);
        await target.Database.EnsureDeletedAsync(ct);

        await mappings.Database.EnsureCreatedAsync(ct);
        await source.Database.EnsureCreatedAsync(ct);
        await target.Database.EnsureCreatedAsync(ct);

        // Seed Products
        var allProducts = Enumerable.Range(1, 10)
            .Select(i => new Product
            {
                Sku = $"SKU-{i:000}",
                Name = $"Product {i}",
                Price = 10 + i
            }).ToList();

        var sourceProducts = allProducts;
        var targetProducts = allProducts.Take(6).Select(p => new Product { Sku = p.Sku, Name = p.Name, Price = p.Price }).ToList();

        source.Products.AddRange(sourceProducts);
        target.Products.AddRange(targetProducts);
        await source.SaveChangesAsync(ct);
        await target.SaveChangesAsync(ct);

        // Seed Customers with Addresses and Orders
        var sourceCustomers = CreateCustomers(5);
        var targetCustomers = CreateCustomers(3);

        source.Customers.AddRange(sourceCustomers);
        target.Customers.AddRange(targetCustomers);
        await source.SaveChangesAsync(ct);
        await target.SaveChangesAsync(ct);

        // Seed Orders and Line Items
        await SeedOrdersAsync(source, sourceCustomers, sourceProducts, ct);
        await SeedOrdersAsync(target, targetCustomers, targetProducts, ct);

        // Build mappings for overlapping entities (by SKU / Email)
        var productSkuToSourceId = await source.Products.AsNoTracking().ToDictionaryAsync(p => p.Sku, p => p.Id, ct);
        var productSkuToTargetId = await target.Products.AsNoTracking().ToDictionaryAsync(p => p.Sku, p => p.Id, ct);

        var customerEmailToSourceId = await source.Customers.AsNoTracking().ToDictionaryAsync(c => c.Email, c => c.Id, ct);
        var customerEmailToTargetId = await target.Customers.AsNoTracking().ToDictionaryAsync(c => c.Email, c => c.Id, ct);

        var mappingRows = new List<EntityMapping>();

        foreach (var sku in productSkuToTargetId.Keys)
        {
            if (productSkuToSourceId.TryGetValue(sku, out var sId))
            {
                mappingRows.Add(new EntityMapping
                {
                    SourceId = sId,
                    TargetId = productSkuToTargetId[sku],
                    EntityName = "Product",
                    DatabaseName = "SalesDb"
                });
            }
        }

        foreach (var email in customerEmailToTargetId.Keys)
        {
            if (customerEmailToSourceId.TryGetValue(email, out var sId))
            {
                mappingRows.Add(new EntityMapping
                {
                    SourceId = sId,
                    TargetId = customerEmailToTargetId[email],
                    EntityName = "Customer",
                    DatabaseName = "SalesDb"
                });
            }
        }

        mappings.EntityMappings.AddRange(mappingRows);
        await mappings.SaveChangesAsync(ct);

        return new
        {
            Source = new
            {
                Customers = await source.Customers.CountAsync(ct),
                Addresses = await source.Addresses.CountAsync(ct),
                Orders = await source.Orders.CountAsync(ct),
                OrderLineItems = await source.OrderLineItems.CountAsync(ct),
                Products = await source.Products.CountAsync(ct),
            },
            Target = new
            {
                Customers = await target.Customers.CountAsync(ct),
                Addresses = await target.Addresses.CountAsync(ct),
                Orders = await target.Orders.CountAsync(ct),
                OrderLineItems = await target.OrderLineItems.CountAsync(ct),
                Products = await target.Products.CountAsync(ct),
            },
            Mappings = new
            {
                Rows = await mappings.EntityMappings.CountAsync(ct)
            }
        };
    }

    private static List<Customer> CreateCustomers(int count)
    {
        var list = new List<Customer>();
        for (int i = 1; i <= count; i++)
        {
            var cust = new Customer
            {
                Name = $"Customer {i}",
                Email = $"customer{i}@example.com",
                Addresses = new List<Address>
                {
                    new Address { Street = $"{i} Main St", City = "Metropolis", State = "NY", PostalCode = $"100{i:00}" },
                    new Address { Street = $"{i} Second Ave", City = "Gotham", State = "NJ", PostalCode = $"070{i:00}" },
                }
            };
            list.Add(cust);
        }
        return list;
    }

    private static async Task SeedOrdersAsync(BaseAppDbContext ctx, List<Customer> customers, List<Product> products, CancellationToken ct)
    {
        var rng = new Random(1234);
        foreach (var c in customers)
        {
            int ordersForCustomer = rng.Next(1, 3);
            for (int o = 0; o < ordersForCustomer; o++)
            {
                var order = new Order
                {
                    CustomerId = c.Id,
                    OrderDate = DateTime.UtcNow.AddDays(-rng.Next(1, 120))
                };

                int items = rng.Next(1, 4);
                for (int j = 0; j < items; j++)
                {
                    var product = products[rng.Next(products.Count)];
                    var qty = rng.Next(1, 5);
                    order.LineItems.Add(new OrderLineItem
                    {
                        ProductId = product.Id,
                        Quantity = qty,
                        UnitPrice = product.Price
                    });
                }
                ctx.Orders.Add(order);
            }
        }
        await ctx.SaveChangesAsync(ct);
    }
}

