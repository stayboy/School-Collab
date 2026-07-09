using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using RabbitMQ.Client;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tests.Unit.Messaging;

[TestClass]
public class OutboxOptionsBindingTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    [TestMethod]
    public void OutboxOptions_BindsFromConfigurationSection()
    {
        // Arrange
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Outbox:ExchangeName"] = "students",
            ["Outbox:BatchSize"] = "50",
            ["Outbox:PollInterval"] = "00:00:05",
        });

        // Act
        var options = config.GetSection(OutboxOptions.SectionName).Get<OutboxOptions>();

        // Assert
        Assert.IsNotNull(options);
        Assert.AreEqual("students", options.ExchangeName);
        Assert.AreEqual(50, options.BatchSize);
        Assert.AreEqual(TimeSpan.FromSeconds(5), options.PollInterval);
    }

    [TestMethod]
    public void OutboxOptions_AppliesDefaultsWhenSectionMissing()
    {
        // Arrange
        var config = BuildConfiguration(new Dictionary<string, string?>());

        // Act
        var options = new OutboxOptions();
        config.GetSection(OutboxOptions.SectionName).Bind(options);

        // Assert
        Assert.AreEqual(100, options.BatchSize);
        Assert.AreEqual(TimeSpan.FromSeconds(1), options.PollInterval);
        Assert.IsNull(options.ExchangeName);
    }
}

[TestClass]
public class OutboxExtensionsTests
{
    [TestMethod]
    public void AddOutbox_BindsOptions_AndRegistersPublisherAndDispatcher()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenancy(); // FR-15: OutboxDispatcher/OutboxIntegrationEventPublisher require ITenantContextAccessor/ITenantProvider
        services.AddDbContextFactory<FakeDbContext>(opt => opt.UseInMemoryDatabase("outbox-extensions-test"));
        // The dispatcher constructor takes a RabbitMQ IConnection. We don't
        // exercise the dispatcher in this test (no broker available); we just
        // need a registered instance so the DI graph builds. A loose Moq
        // default is sufficient.
        services.AddSingleton(Mock.Of<IConnection>());
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Outbox:ExchangeName"] = "test-exchange",
            ["Outbox:BatchSize"] = "25",
            ["Outbox:PollInterval"] = "00:00:02",
        });

        // Act
        services.AddOutbox<FakeDbContext>(config);
        using var provider = services.BuildServiceProvider();

        // Assert — options bound from configuration
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<OutboxOptions>>();
        var options = optionsMonitor.CurrentValue;
        Assert.AreEqual("test-exchange", options.ExchangeName);
        Assert.AreEqual(25, options.BatchSize);
        Assert.AreEqual(TimeSpan.FromSeconds(2), options.PollInterval);

        // Assert — publisher registered against the shared contract
        var publisher = provider.GetRequiredService<IIntegrationEventPublisher>();
        Assert.IsInstanceOfType<OutboxIntegrationEventPublisher<FakeDbContext>>(publisher);

        // Assert — dispatcher registered as a hosted service
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        var dispatcher = hostedServices
            .Select(s => s.GetType().GetGenericArguments()[0])
            .FirstOrDefault(t => t == typeof(FakeDbContext));
        Assert.IsNotNull(dispatcher,
            $"Expected an OutboxDispatcher<FakeDbContext> hosted service. " +
            $"Found: {string.Join(", ", hostedServices.Select(s => s.GetType().Name))}");
    }

    [TestMethod]
    public void AddOutbox_UsesDefaultSectionName_WhenNotOverridden()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContextFactory<FakeDbContext>(opt => opt.UseInMemoryDatabase("outbox-extensions-default-section"));
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Outbox:ExchangeName"] = "default-section",
        });

        // Act
        services.AddOutbox<FakeDbContext>(config);
        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptionsMonitor<OutboxOptions>>().CurrentValue;
        Assert.AreEqual("default-section", options.ExchangeName);
    }

    [TestMethod]
    public void AddOutbox_HonoursCustomSectionName()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContextFactory<FakeDbContext>(opt => opt.UseInMemoryDatabase("outbox-extensions-custom-section"));
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Outbox:ExchangeName"] = "wrong-section",
            ["MyCustom:ExchangeName"] = "right-section",
        });

        // Act
        services.AddOutbox<FakeDbContext>(config, sectionName: "MyCustom");
        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptionsMonitor<OutboxOptions>>().CurrentValue;
        Assert.AreEqual("right-section", options.ExchangeName);
    }

    [TestMethod]
    public void AddOutbox_ThrowsArgumentNullException_WhenServicesIsNull()
    {
        // Arrange
        IServiceCollection? services = null;
        var config = BuildConfiguration(new Dictionary<string, string?>());

        // Act + Assert
        Assert.ThrowsExactly<ArgumentNullException>(
            () => services!.AddOutbox<FakeDbContext>(config));
    }

    [TestMethod]
    public void AddOutbox_ThrowsArgumentNullException_WhenConfigurationIsNull()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act + Assert
        Assert.ThrowsExactly<ArgumentNullException>(
            () => services.AddOutbox<FakeDbContext>(configuration: null!));
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    /// <summary>
    /// Minimal DbContext used to exercise the generic
    /// <see cref="OutboxIntegrationEventPublisher{TContext}"/> and
    /// <see cref="OutboxDispatcher{TContext}"/> bindings without any
    /// domain-specific configuration. In-memory so it doesn't need a
    /// real database.
    /// </summary>
    private sealed class FakeDbContext : DbContext
    {
        public FakeDbContext(DbContextOptions<FakeDbContext> options) : base(options) { }

        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    }
}
