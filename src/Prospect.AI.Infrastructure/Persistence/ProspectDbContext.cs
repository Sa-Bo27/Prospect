using Microsoft.EntityFrameworkCore;
using Prospect.AI.Domain.Common;

namespace Prospect.AI.Infrastructure.Persistence;

public sealed class ProspectDbContext(DbContextOptions<ProspectDbContext> options, ICurrentUser currentUser)
    : DbContext(options)
{
    // Read by the owner query filter on every query (EF Core parameterizes DbContext members).
    private Guid? CurrentUserId => currentUser.UserId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Entity configurations live next to this file, one IEntityTypeConfiguration<T> per entity.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProspectDbContext).Assembly);

        ApplyOwnerQueryFilters(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AssignOwnerToNewEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AssignOwnerToNewEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// Every owned entity is filtered to the current user. Without a user, queries return nothing;
    /// background jobs that work across users must opt out explicitly with IgnoreQueryFilters().
    /// </summary>
    private void ApplyOwnerQueryFilters(ModelBuilder modelBuilder)
    {
        var applyFilter = typeof(ProspectDbContext).GetMethod(
            nameof(ApplyOwnerQueryFilter),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IOwnedEntity).IsAssignableFrom(entityType.ClrType) && entityType.BaseType is null)
            {
                applyFilter.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ApplyOwnerQueryFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IOwnedEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.OwnerId == CurrentUserId);
        modelBuilder.Entity<TEntity>().HasIndex(entity => entity.OwnerId);
    }

    private void AssignOwnerToNewEntities()
    {
        foreach (var entry in ChangeTracker.Entries<IOwnedEntity>())
        {
            if (entry.State != EntityState.Added || entry.Entity.OwnerId != Guid.Empty)
            {
                continue;
            }

            entry.Entity.OwnerId = CurrentUserId
                ?? throw new InvalidOperationException(
                    $"Cannot save a new {entry.Entity.GetType().Name} without a current user or an explicit OwnerId.");
        }
    }
}
