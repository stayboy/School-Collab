using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.SignatureConsentTexts.Commands.UpsertTenantSignatureConsentText;
using SchoolCollab.Settings.Core.CQRS.SignatureConsentTexts.Queries.GetTenantSignatureConsentText;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Tests.Unit;

[TestClass]
public class TenantSignatureConsentTextTests
{
    private static readonly Guid TenantId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [TestMethod]
    public void Create_FactoryTrims()
    {
        var row = TenantSignatureConsentText.Create(TenantId, "  By signing below I confirm review.  ");
        row.TenantId.Should().Be(TenantId);
        row.ConsentText.Should().Be("By signing below I confirm review.");
    }

    [TestMethod]
    public void Create_NullRestoresEmbeddedDefault()
    {
        var row = TenantSignatureConsentText.Create(TenantId, consentText: null);
        row.ConsentText.Should().BeNull();
    }

    [TestMethod]
    public void SetConsentText_RejectsOver4000Chars()
    {
        var row = TenantSignatureConsentText.Create(TenantId);
        var act = () => row.SetConsentText(new string('x', TenantSignatureConsentText.MaxConsentTextLength + 1));
        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void SetConsentText_TrimsAndStampsUpdatedAt()
    {
        var row = TenantSignatureConsentText.Create(TenantId);
        var before = row.UpdatedAt;

        row.SetConsentText("  Revised consent language.  ");

        row.ConsentText.Should().Be("Revised consent language.");
        row.UpdatedAt.Should().BeOnOrAfter(before);
    }
}

[TestClass]
public class TenantSignatureConsentTextHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private SettingsDbContext _db = default!;
    private TenantProvider _tenants = default!;
    private GetTenantSignatureConsentTextHandler _getHandler = default!;
    private UpsertTenantSignatureConsentTextHandler _upsertHandler = default!;

    [TestInitialize]
    public void Setup()
    {
        _tenants = new TenantProvider();
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseInMemoryDatabase($"SignatureConsentText_{Guid.NewGuid()}")
            .Options;
        _db = new SettingsDbContext(options, _tenants);
        _getHandler = new GetTenantSignatureConsentTextHandler(_db);
        _upsertHandler = new UpsertTenantSignatureConsentTextHandler(_db, _tenants);
    }

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    private void AsTenant(Guid tenantId) =>
        _tenants.SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));

    [TestMethod]
    public async Task GetHandler_ReturnsNullWhenMissing()
    {
        AsTenant(TenantA);
        var result = await _getHandler.HandleAsync(GetTenantSignatureConsentText.Instance);
        result.Should().BeNull();
    }

    [TestMethod]
    public async Task UpsertHandler_InsertsThenUpdates()
    {
        AsTenant(TenantA);
        await _upsertHandler.HandleAsync(new UpsertTenantSignatureConsentText("First consent text."));

        var afterInsert = await _getHandler.HandleAsync(GetTenantSignatureConsentText.Instance);
        afterInsert.Should().NotBeNull();
        afterInsert!.ConsentText.Should().Be("First consent text.");

        await _upsertHandler.HandleAsync(new UpsertTenantSignatureConsentText("Second consent text."));

        var afterUpdate = await _getHandler.HandleAsync(GetTenantSignatureConsentText.Instance);
        afterUpdate!.ConsentText.Should().Be("Second consent text.");
        _db.TenantSignatureConsentTexts.Count().Should().Be(1);
    }

    [TestMethod]
    public async Task UpsertHandler_RejectsOverCap()
    {
        AsTenant(TenantA);
        var act = () => _upsertHandler.HandleAsync(
            new UpsertTenantSignatureConsentText(new string('x', TenantSignatureConsentText.MaxConsentTextLength + 1)));
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
