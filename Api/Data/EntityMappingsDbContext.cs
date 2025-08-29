using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class EntityMappingsDbContext : DbContext
{
    public EntityMappingsDbContext(DbContextOptions<EntityMappingsDbContext> options) : base(options)
    {
    }

    public DbSet<EntityMapping> EntityMappings => Set<EntityMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<EntityMapping>(entity =>
        {
            entity.ToTable("EntityMappings");
            entity.Property(p => p.EntityName).IsRequired().HasMaxLength(200);
            entity.Property(p => p.DatabaseName).IsRequired().HasMaxLength(200);
            entity.HasKey(p => new { p.SourceId, p.TargetId, p.EntityName, p.DatabaseName });
        });
    }
}

public class EntityMapping
{
    public int SourceId { get; set; }
    public int TargetId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
}

