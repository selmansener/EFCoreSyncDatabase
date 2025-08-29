using System.Linq.Expressions;
using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public static class GenericSyncService
{
    private const string DatabaseName = "SalesDb";

    public static async Task<object> SyncAsync(IServiceProvider sp, string entityName, int sourceId, CancellationToken ct = default)
    {
        using var scope = sp.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<SourceDbContext>();
        var target = scope.ServiceProvider.GetRequiredService<TargetDbContext>();
        var mappings = scope.ServiceProvider.GetRequiredService<EntityMappingsDbContext>();

        var srcEntityType = ResolveEntityType(source, entityName);
        if (srcEntityType is null)
            return new { Status = "error", Message = $"Entity '{entityName}' not found in source model." };

        var visited = new HashSet<(Type ClrType, int SourceId)>();

        var entity = await GetEntityByIdAsync(source, srcEntityType, sourceId, ct);
        if (entity is null)
            return new { Status = "not_found", Entity = srcEntityType.ClrType.Name, SourceId = sourceId };

        var details = new List<object>();
        await SyncEntityGraphAsync(source, target, mappings, srcEntityType, entity, visited, details, ct);

        return new { Status = "ok", Entity = srcEntityType.ClrType.Name, SourceId = sourceId, Synced = details };
    }

    private static async Task SyncEntityGraphAsync(DbContext source, DbContext target, EntityMappingsDbContext mappings,
        Microsoft.EntityFrameworkCore.Metadata.IEntityType srcType, object srcEntity,
        HashSet<(Type, int)> visited, List<object> details, CancellationToken ct)
    {
        var srcId = (int)(srcType.FindPrimaryKey()!.Properties[0].PropertyInfo!.GetValue(srcEntity)!);
        if (!visited.Add((srcType.ClrType, srcId))) return;

        var entry = source.Entry(srcEntity);

        // 1) Load and sync principals first (references where this entity is dependent)
        foreach (var nav in srcType.GetNavigations())
        {
            if (!nav.IsOnDependent) continue; // we only want references to principals
            if (nav.IsCollection) continue;
            await entry.Reference(nav.Name).LoadAsync(ct);
            var principal = entry.Reference(nav.Name).CurrentValue;
            if (principal is null) continue;
            await SyncEntityGraphAsync(source, target, mappings, nav.TargetEntityType, principal, visited, details, ct);
        }

        // 2) Upsert this entity in target
        var upsertResult = await UpsertEntityAsync(source, target, mappings, srcType, srcEntity, ct);
        details.Add(upsertResult);

        // 3) Load and sync dependents (collections and references where we are principal)
        foreach (var nav in srcType.GetNavigations())
        {
            if (nav.IsOnDependent && !nav.IsCollection) continue; // already handled principal refs

            if (nav.IsCollection)
            {
                await entry.Collection(nav.Name).LoadAsync(ct);
                var children = entry.Collection(nav.Name).CurrentValue;
                if (children is IEnumerable<object> list)
                {
                    foreach (var child in list)
                    {
                        await SyncEntityGraphAsync(source, target, mappings, nav.TargetEntityType, child, visited, details, ct);
                    }
                }
            }
            else
            {
                await entry.Reference(nav.Name).LoadAsync(ct);
                var child = entry.Reference(nav.Name).CurrentValue;
                if (child is not null)
                {
                    await SyncEntityGraphAsync(source, target, mappings, nav.TargetEntityType, child, visited, details, ct);
                }
            }
        }
    }

    private static async Task<object> UpsertEntityAsync(DbContext source, DbContext target, EntityMappingsDbContext mappings,
        Microsoft.EntityFrameworkCore.Metadata.IEntityType srcType, object srcEntity, CancellationToken ct)
    {
        var clr = srcType.ClrType;
        var keyProp = srcType.FindPrimaryKey()!.Properties[0].PropertyInfo!;
        var sourceId = (int)(keyProp.GetValue(srcEntity)!);
        var entityName = clr.Name;

        // Ensure FK scalar properties point to mapped target ids (principals already synced)
        var fkProps = srcType.GetForeignKeys().SelectMany(fk => fk.Properties).Select(p => p.PropertyInfo!).ToHashSet();

        // Prepare a detached target instance with values copied
        var targetType = target.Model.FindEntityType(clr)!;
        var targetInstance = Activator.CreateInstance(clr)!;

        foreach (var prop in srcType.GetProperties())
        {
            var pi = prop.PropertyInfo!;
            if (prop.IsPrimaryKey()) continue; // handle key logic later
            if (pi is null) continue;

            object? value;
            if (fkProps.Contains(pi))
            {
                // translate FK value using mappings
                var srcFkVal = pi.GetValue(srcEntity);
                if (srcFkVal is int fkSourceId)
                {
                    var principalName = prop.GetContainingForeignKeys().First().PrincipalEntityType.ClrType.Name;
                    var map = await mappings.EntityMappings.AsNoTracking()
                        .FirstOrDefaultAsync(m => m.SourceId == fkSourceId && m.EntityName == principalName && m.DatabaseName == DatabaseName, ct);
                    value = map?.TargetId ?? 0; // if still missing, default 0
                }
                else
                {
                    value = srcFkVal;
                }
            }
            else
            {
                value = pi.GetValue(srcEntity);
            }
            targetType.FindProperty(prop.Name)!.PropertyInfo!.SetValue(targetInstance, value);
        }

        // Mapping lookup
        var existingMap = await mappings.EntityMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.SourceId == sourceId && m.EntityName == entityName && m.DatabaseName == DatabaseName, ct);

        if (existingMap is not null)
        {
            // Update existing target entity using mapped TargetId
            var tgt = await GetEntityByIdAsync(target, targetType, existingMap.TargetId, ct);
            if (tgt is null)
            {
                // mapping stale, reinsert with mapped id
                SetKey(targetType, targetInstance, existingMap.TargetId);
                await InsertWithIdentityAsync(target, targetType, targetInstance, ct);
            }
            else
            {
                CopyScalars(targetType, targetInstance, tgt);
                target.Update(tgt);
                await target.SaveChangesAsync(ct);
            }
            return new { Entity = entityName, Action = "updated", SourceId = sourceId, TargetId = existingMap.TargetId };
        }

        // No mapping yet: check if target has same primary key
        var samePk = await GetEntityByIdAsync(target, targetType, sourceId, ct);
        if (samePk is not null)
        {
            // Insert with new identity id
            ClearKey(targetType, targetInstance);
            target.Add(targetInstance);
            await target.SaveChangesAsync(ct);

            var newId = (int)targetType.FindPrimaryKey()!.Properties[0].PropertyInfo!.GetValue(targetInstance)!;
            await AddOrUpdateMappingAsync(mappings, entityName, sourceId, newId, ct);
            return new { Entity = entityName, Action = "inserted_identity", SourceId = sourceId, TargetId = newId };
        }
        else
        {
            // Insert with source id (requires IDENTITY_INSERT)
            SetKey(srcType, targetInstance, sourceId);
            await InsertWithIdentityAsync(target, targetType, targetInstance, ct);
            await AddOrUpdateMappingAsync(mappings, entityName, sourceId, sourceId, ct);
            return new { Entity = entityName, Action = "inserted_with_id", SourceId = sourceId, TargetId = sourceId };
        }
    }

    private static void CopyScalars(Microsoft.EntityFrameworkCore.Metadata.IEntityType et, object from, object to)
    {
        foreach (var prop in et.GetProperties())
        {
            if (prop.IsPrimaryKey()) continue;
            var pi = prop.PropertyInfo!;
            pi.SetValue(to, pi.GetValue(from));
        }
    }

    private static void SetKey(Microsoft.EntityFrameworkCore.Metadata.IEntityType et, object instance, int id)
    {
        var pi = et.FindPrimaryKey()!.Properties[0].PropertyInfo!;
        pi.SetValue(instance, id);
    }

    private static void ClearKey(Microsoft.EntityFrameworkCore.Metadata.IEntityType et, object instance)
    {
        var pi = et.FindPrimaryKey()!.Properties[0].PropertyInfo!;
        if (pi.PropertyType == typeof(int)) pi.SetValue(instance, 0);
    }

    private static async Task InsertWithIdentityAsync(DbContext target, Microsoft.EntityFrameworkCore.Metadata.IEntityType et, object instance, CancellationToken ct)
    {
        var table = et.GetTableName();
        var schema = et.GetSchema() ?? "dbo";
        await using var tx = await target.Database.BeginTransactionAsync(ct);
        await target.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT [{schema}].[{table}] ON;", ct);
        target.Add(instance);
        await target.SaveChangesAsync(ct);
        await target.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT [{schema}].[{table}] OFF;", ct);
        await tx.CommitAsync(ct);
    }

    private static async Task AddOrUpdateMappingAsync(EntityMappingsDbContext mappings, string entityName, int sourceId, int targetId, CancellationToken ct)
    {
        var existing = await mappings.EntityMappings
            .FirstOrDefaultAsync(m => m.SourceId == sourceId && m.EntityName == entityName && m.DatabaseName == DatabaseName, ct);
        if (existing is null)
        {
            mappings.EntityMappings.Add(new EntityMapping { SourceId = sourceId, TargetId = targetId, EntityName = entityName, DatabaseName = DatabaseName });
        }
        else
        {
            existing.TargetId = targetId;
            mappings.EntityMappings.Update(existing);
        }
        await mappings.SaveChangesAsync(ct);
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IEntityType? ResolveEntityType(DbContext ctx, string entityName)
        => ctx.Model.GetEntityTypes().FirstOrDefault(e => string.Equals(e.ClrType.Name, entityName, StringComparison.OrdinalIgnoreCase)
                                                       || string.Equals(e.ClrType.FullName, entityName, StringComparison.OrdinalIgnoreCase));

    private static async Task<object?> GetEntityByIdAsync(DbContext ctx, Microsoft.EntityFrameworkCore.Metadata.IEntityType et, int id, CancellationToken ct)
    {
        var key = et.FindPrimaryKey()!;
        if (key.Properties.Count != 1)
            throw new NotSupportedException($"Composite keys are not supported for '{et.ClrType.Name}'.");
        var keyName = key.Properties[0].Name;

        // Build e => EF.Property<int>(e, keyName) == id
        var param = Expression.Parameter(et.ClrType, "e");
        var propAccess = Expression.Call(typeof(EF), nameof(EF.Property), new[] { typeof(int) }, param, Expression.Constant(keyName));
        var body = Expression.Equal(propAccess, Expression.Constant(id));
        var lambda = Expression.Lambda(body, param);

        var set = ctx.GetType().GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(et.ClrType).Invoke(ctx, null)!;
        var queryExpr = Expression.Call(typeof(Queryable), nameof(Queryable.Where), new[] { et.ClrType }, ((IQueryable)set).Expression, lambda);
        var query = ((IQueryable)set).Provider.CreateQuery(queryExpr);

        // Execute synchronously for simplicity
        var enumerator = query.GetEnumerator();
        using (enumerator as IDisposable)
        {
            if (enumerator.MoveNext()) return enumerator.Current;
        }
        return null;
    }
}

