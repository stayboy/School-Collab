using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Tenancy;

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
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.ConfigureGuidId();
        ConfigureEntity(builder);
    }

    /// <summary>
    /// Configures entity-specific table, column, relationship, owned type, and index mappings.
    /// </summary>
    /// <param name="builder">The EF Core entity type builder.</param>
    protected abstract void ConfigureEntity(EntityTypeBuilder<TEntity> builder);
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
    /// Adds the standard soft-delete query filter.
    /// </summary>
    public static void ConfigureSoftDeleteQueryFilter<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ISoftDeletableEntity
    {
        builder.HasQueryFilter(entity => !EF.Property<bool>(entity, nameof(ISoftDeletableEntity.IsDeleted)));
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
