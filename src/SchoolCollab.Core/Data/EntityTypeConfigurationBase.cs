using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Tenancy;
using System.Linq.Expressions;

namespace SchoolCollab.Core.Data;

/// <summary>
/// Minimal contract for EF Core entities that use a GUID primary key named <c>Id</c>.
/// </summary>
public interface IEntity
{
    /// <summary>The entity's stable identifier.</summary>
    Guid Id { get; }
}

/// <summary>
/// Contract for entities that track creation and modification timestamps.
/// </summary>
public interface IAuditableEntity
{
    /// <summary>The UTC timestamp when the entity was created.</summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>The UTC timestamp when the entity was last modified.</summary>
    DateTimeOffset UpdatedAt { get; }
}

/// <summary>
/// Contract for soft-deletable entities.
/// </summary>
public interface ISoftDeletableEntity : IAuditableEntity
{
    /// <summary>Whether the entity has been soft-deleted.</summary>
    bool IsDeleted { get; }

    /// <summary>The UTC timestamp when the entity was soft-deleted, if applicable.</summary>
    DateTimeOffset? DeletedAt { get; }
}

/// <summary>
/// Contract for entities that use PostgreSQL's system <c>xmin</c> column for optimistic concurrency.
/// </summary>
public interface IHasRowVersion
{
    /// <summary>PostgreSQL row version value.</summary>
    uint RowVersion { get; }
}

/// <summary>
/// Base EF Core configuration for module entity configurations.
/// </summary>
/// <typeparam name="TEntity">The entity type being configured.</typeparam>
public abstract class EntityTypeConfigurationBase<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : class, IEntity
{
    /// <inheritdoc />
    public virtual void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.ConfigureGuidId();
        ConfigureEntity(builder);
    }

    /// <summary>
    /// Configures entity-specific table, column, relationship, owned type, and index mappings.
    /// Override this method in derived configuration classes.
    /// </summary>
    /// <param name="builder">The EF Core entity type builder.</param>
    protected virtual void ConfigureEntity(EntityTypeBuilder<TEntity> builder)
    {
    }
}

/// <summary>
/// Base EF Core configuration for tenant-scoped entities. Applies the tenant column mapping
/// and the named "Tenant" global query filter.
/// </summary>
/// <typeparam name="TEntity">The tenant-scoped entity type.</typeparam>
public abstract class TenantEntityTypeConfigurationBase<TEntity>
    : EntityTypeConfigurationBase<TEntity>
    where TEntity : class, IEntity, ITenantEntity
{
    private readonly Expression<Func<Guid>> _tenantIdAccessor;

    /// <summary>
    /// Initialises a new instance of the tenant-scoped configuration base class.
    /// </summary>
    /// <param name="tenantIdAccessor">
    /// An expression that returns the current tenant id from the active <see cref="DbContext"/>.
    /// The expression body is spliced into the query filter so EF Core evaluates it per query.
    /// </param>
    protected TenantEntityTypeConfigurationBase(Expression<Func<Guid>> tenantIdAccessor) =>
        _tenantIdAccessor = tenantIdAccessor;

    /// <inheritdoc />
    public sealed override void Configure(EntityTypeBuilder<TEntity> builder)
    {
        base.Configure(builder);
        builder.ConfigureTenantProperties();
        builder.ConfigureTenantQueryFilter(_tenantIdAccessor);
        ConfigureTenantEntity(builder);
    }

    /// <summary>
    /// Configures entity-specific table, column, relationship, owned type, and index mappings.
    /// </summary>
    /// <param name="builder">The EF Core entity type builder.</param>
    protected abstract void ConfigureTenantEntity(EntityTypeBuilder<TEntity> builder);
}

/// <summary>
/// Shared EF Core mapping helpers for common entity conventions.
/// </summary>
public static class EntityTypeBuilderExtensions
{
    /// <summary>
    /// Maps the conventional GUID <c>Id</c> primary key and prevents database value generation.
    /// </summary>
    public static void ConfigureGuidId<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IEntity
    {
        builder.HasKey("Id");
        builder.Property<Guid>("Id").ValueGeneratedNever();
    }

    /// <summary>
    /// Maps required audit timestamp columns.
    /// </summary>
    public static void ConfigureAuditProperties<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IAuditableEntity
    {
        builder.Property<DateTimeOffset>("CreatedAt").IsRequired();
        builder.Property<DateTimeOffset>("UpdatedAt").IsRequired();
    }

    /// <summary>
    /// Maps tenant isolation columns for tenant-scoped aggregates.
    /// </summary>
    public static void ConfigureTenantProperties<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ITenantEntity
    {
        builder.Property<Guid>("TenantId").IsRequired();
    }

    /// <summary>
    /// Maps soft-delete marker columns.
    /// </summary>
    public static void ConfigureSoftDeleteProperties<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ISoftDeletableEntity
    {
        builder.Property<bool>("IsDeleted").HasDefaultValue(false);
        builder.Property<DateTimeOffset?>("DeletedAt");
    }

    /// <summary>
    /// Adds the standard soft-delete query filter using the named filter "SoftDelete" so it can
    /// be disabled independently of other filters (requires EF Core 10+).
    /// </summary>
    public static void ConfigureSoftDeleteQueryFilter<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ISoftDeletableEntity
    {
        builder.HasQueryFilter("SoftDelete", entity => !entity.IsDeleted);
    }

    /// <summary>
    /// Adds the standard tenant isolation query filter using the named filter "Tenant".
    /// The tenant id accessor expression is spliced into the filter so EF Core evaluates it
    /// per query. This avoids hard-coding a constant in the cached EF model.
    /// </summary>
    public static void ConfigureTenantQueryFilter<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<Guid>> tenantIdAccessor)
        where TEntity : class, ITenantEntity
    {
        var entityParam = Expression.Parameter(typeof(TEntity), "entity");
        var tenantIdProperty = typeof(ITenantEntity).GetProperty(nameof(ITenantEntity.TenantId))!;
        var left = Expression.Property(entityParam, tenantIdProperty);
        var body = Expression.Equal(left, tenantIdAccessor.Body);
        var lambda = Expression.Lambda<Func<TEntity, bool>>(body, entityParam);
        builder.HasQueryFilter("Tenant", lambda);
    }

    /// <summary>
    /// Maps a PostgreSQL <c>xmin</c> row version column for optimistic concurrency.
    /// </summary>
    public static void ConfigurePostgresRowVersion<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IHasRowVersion
    {
        builder.Property<uint>("RowVersion")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();
    }
}
