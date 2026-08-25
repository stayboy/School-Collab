using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// FlagRoutedCodedValuesApiClient routes GetByIdAsync to the local read model when
/// Students:UseLocalCodedValueProjection is on and to the HTTP client when off —
/// and must never touch the path that isn't selected (adr-cross-module-calls.md Phase 1).
/// </summary>
[TestClass]
public class FlagRoutedCodedValuesApiClientTests
{
    private const string FlagKey = "Students:UseLocalCodedValueProjection";

    private static IConfiguration Config(string flagValue)
    {
        // Cover both access patterns the GetValue<bool> binder may use:
        // the indexer and GetSection(...).Value.
        var cfg = new Mock<IConfiguration>();
        var section = new Mock<IConfigurationSection>();
        section.SetupGet(s => s.Value).Returns(flagValue);
        cfg.Setup(c => c.GetSection(FlagKey)).Returns(section.Object);
        cfg.SetupGet(c => c[FlagKey]).Returns(flagValue);
        return cfg.Object;
    }

    private static StreamCodedValueDto Dto() => new(
        Guid.NewGuid(), "GRADE7", "Year 7", null, null, "GRADE", false, 7,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Array.Empty<StreamAttributeDto>());

    [TestMethod]
    public async Task FlagOff_DelegatesToHttpClient_NeverTouchesLocal()
    {
        var cv = Dto();
        var http = new Mock<ICodedValuesApiClient>();
        http.Setup(h => h.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(cv);
        var local = new Mock<ILocalCodedValueRepository>();
        local.Setup(l => l.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StreamCodedValueDto?)null);

        var sut = new FlagRoutedCodedValuesApiClient(http.Object, local.Object, Config("false"));
        var result = await sut.GetByIdAsync(Guid.NewGuid());

        result.Should().BeSameAs(cv);
        http.Verify(h => h.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        local.Verify(l => l.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task FlagOn_DelegatesToLocalRepository_NeverTouchesHttp()
    {
        var cv = Dto();
        var http = new Mock<ICodedValuesApiClient>();
        http.Setup(h => h.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(cv);
        var local = new Mock<ILocalCodedValueRepository>();
        local.Setup(l => l.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(cv);

        var sut = new FlagRoutedCodedValuesApiClient(http.Object, local.Object, Config("true"));
        var result = await sut.GetByIdAsync(Guid.NewGuid());

        result.Should().BeSameAs(cv);
        local.Verify(l => l.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        http.Verify(h => h.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task FlagUnset_DefaultsToHttp()
    {
        // No config value present — GetValue<bool>(..., defaultValue:false) → HTTP path.
        var cv = Dto();
        var http = new Mock<ICodedValuesApiClient>();
        http.Setup(h => h.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(cv);
        var local = new Mock<ILocalCodedValueRepository>();

        var cfg = new Mock<IConfiguration>();
        // GetSection returns a default section with null Value → Exists() false → defaultValue used.
        cfg.Setup(c => c.GetSection(FlagKey)).Returns(new Mock<IConfigurationSection>().Object);

        var sut = new FlagRoutedCodedValuesApiClient(http.Object, local.Object, cfg.Object);
        var result = await sut.GetByIdAsync(Guid.NewGuid());

        result.Should().BeSameAs(cv);
        http.Verify(h => h.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        local.Verify(l => l.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}