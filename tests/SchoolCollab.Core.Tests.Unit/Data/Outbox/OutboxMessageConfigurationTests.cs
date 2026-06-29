using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Core.Tests.Unit.Data.Outbox;

[TestClass]
public class OutboxMessageConfigurationTests
{
    /// <summary>
    /// Builds a real relational <see cref="IModel"/> from a fresh
    /// SQLite in-memory database and applies the
    /// <see cref="OutboxMessageConfiguration"/> against it. SQLite
    /// is relational, so the model carries the same annotations
    /// (max length, column type, default value) that a production
    /// provider like Npgsql would set.
    ///
    /// EF Core's <see cref="IModelCacheKeyFactory"/> is keyed on the
    /// <c>DbContext</c> type and the <see cref="DbContextOptions"/>.
    /// A test that uses the same DbContext type with different
    /// <see cref="OutboxConfigurationFlags"/> will receive the same
    /// cached model — losing the per-test deltas. We side-step this
    /// by deriving a unique <c>DbContext</c> subclass per flags
    /// value, which gives each test its own cache slot.
    /// </summary>
    private static IModel BuildModel(OutboxConfigurationFlags flags, Type dbContextType)
    {
        var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        try
        {
            var options = new DbContextOptionsBuilder<DbContext>()
                .UseSqlite(connection)
                .Options;
            using var dbContext = (DbContext)Activator.CreateInstance(
                dbContextType, options, flags)!;
            return dbContext.Model;
        }
        finally
        {
            connection.Close();
        }
    }

    [TestMethod]
    public void ConfigureEntity_AppliesDefaultShape_WhenUsingDefaultFlags()
    {
        // Arrange
        var model = BuildModel(OutboxConfigurationFlags.Default, typeof(DefaultFlagsDbContext));

        // Assert
        var entity = model.FindEntityType(typeof(OutboxMessage))!;
        Assert.AreEqual("outbox_messages", entity.GetTableName());

        // Required columns
        Assert.IsFalse(entity.FindProperty(nameof(OutboxMessage.OccurredAt)).IsNullable);
        Assert.IsFalse(entity.FindProperty(nameof(OutboxMessage.Type)).IsNullable);
        Assert.IsFalse(entity.FindProperty(nameof(OutboxMessage.Payload)).IsNullable);
        Assert.IsFalse(entity.FindProperty(nameof(OutboxMessage.Attempts)).IsNullable);

        // Optional columns
        Assert.IsTrue(entity.FindProperty(nameof(OutboxMessage.DispatchedAt)).IsNullable);
        Assert.IsTrue(entity.FindProperty(nameof(OutboxMessage.LastError)).IsNullable);

        // Type max length
        Assert.AreEqual(200, entity.FindProperty(nameof(OutboxMessage.Type)).GetMaxLength());

        // No "jsonb" column-type override on Payload (SQLite may set a
        // default text column type on string properties, which is fine).
        Assert.AreNotEqual("jsonb", entity.FindProperty(nameof(OutboxMessage.Payload)).GetColumnType());

        // Default indexes
        var indexNames = entity.GetIndexes().Select(i => i.GetDatabaseName()).ToList();
        CollectionAssert.Contains(indexNames, "ix_outbox_messages_dispatched_at");
        CollectionAssert.Contains(indexNames, "ix_outbox_messages_occurred_at");
    }

    [TestMethod]
    public void ConfigureEntity_AppliesCodedValuesFlags_WhenUsePartialIndexAndJsonbRequested()
    {
        // Arrange — the CodedValues case
        var flags = new OutboxConfigurationFlags(
            TypeMaxLength: 500,
            PayloadColumnType: "jsonb",
            AttemptsDefaultValue: 0,
            UsePartialIndex: true);
        var model = BuildModel(flags, typeof(CodedValuesFlagsDbContext));

        // Assert
        var entity = model.FindEntityType(typeof(OutboxMessage))!;

        // CodedValues-specific knobs
        Assert.AreEqual(500, entity.FindProperty(nameof(OutboxMessage.Type)).GetMaxLength());
        Assert.AreEqual("jsonb", entity.FindProperty(nameof(OutboxMessage.Payload)).GetColumnType());
        Assert.AreEqual(0, entity.FindProperty(nameof(OutboxMessage.Attempts)).GetDefaultValue());

        // Partial index replaces the default non-filtered indexes
        var indexNames = entity.GetIndexes().Select(i => i.GetDatabaseName()).ToList();
        CollectionAssert.Contains(indexNames, "ix_outbox_messages_pending");
        CollectionAssert.DoesNotContain(indexNames, "ix_outbox_messages_dispatched_at");
        CollectionAssert.DoesNotContain(indexNames, "ix_outbox_messages_occurred_at");

        var pendingIndex = entity.GetIndexes()
            .Single(i => i.GetDatabaseName() == "ix_outbox_messages_pending");
        Assert.AreEqual("dispatched_at IS NULL", pendingIndex.GetFilter());
    }

    [TestMethod]
    public void ConfigureEntity_PreservesGuards_ForNullFlags()
    {
        // Arrange + Act + Assert
        Assert.ThrowsExactly<ArgumentNullException>(() => new OutboxMessageConfiguration(flags: null!));
    }

    [TestMethod]
    public void OutboxConfigurationBuilder_FluentChain_ProducesExpectedFlags()
    {
        // Act
        var flags = OutboxConfigurationFlags.FromConfiguration(b => b
            .SetTypeMaxLength(500)
            .UseJsonbPayload()
            .UseAttemptsDefaultZero()
            .UsePartialIndexOnOccurredAt());

        // Assert
        Assert.AreEqual(500, flags.TypeMaxLength);
        Assert.AreEqual("jsonb", flags.PayloadColumnType);
        Assert.AreEqual(0, flags.AttemptsDefaultValue);
        Assert.IsTrue(flags.UsePartialIndex);
    }

    [TestMethod]
    public void OutboxConfigurationBuilder_NullConfigure_ReturnsDefaults()
    {
        // Act
        var flags = OutboxConfigurationFlags.FromConfiguration(configure: null);

        // Assert
        Assert.AreEqual(OutboxConfigurationFlags.Default, flags);
    }

    [TestMethod]
    public void OutboxConfigurationBuilder_RejectsNonPositiveTypeMaxLength()
    {
        // Arrange
        var builder = new OutboxConfigurationBuilder();

        // Act + Assert
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => builder.SetTypeMaxLength(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => builder.SetTypeMaxLength(-1));
    }

    /// <summary>
    /// Base <see cref="DbContext"/> for all outbox-configuration
    /// tests. Subclasses exist purely to give EF Core's model cache
    /// a different slot per test (so the per-test
    /// <see cref="OutboxConfigurationFlags"/> take effect rather
    /// than being overridden by a cached model from a previous test).
    /// </summary>
    private abstract class TestDbContextBase : DbContext
    {
        private readonly OutboxConfigurationFlags _flags;

        protected TestDbContextBase(DbContextOptions options, OutboxConfigurationFlags flags)
            : base(options)
        {
            _flags = flags;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(_flags));
        }
    }

    private sealed class DefaultFlagsDbContext : TestDbContextBase
    {
        public DefaultFlagsDbContext(DbContextOptions options, OutboxConfigurationFlags flags)
            : base(options, flags) { }
    }

    private sealed class CodedValuesFlagsDbContext : TestDbContextBase
    {
        public CodedValuesFlagsDbContext(DbContextOptions options, OutboxConfigurationFlags flags)
            : base(options, flags) { }
    }
}
