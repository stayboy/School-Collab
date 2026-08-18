using System.Linq;
using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Constants;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Core.Contracts;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the lifecycle of the shared <c>ContactsEditor</c>
/// component, focusing on the post-review disposability fixes:
/// <list type="bullet">
///   <item>The load uses a <see cref="CancellationTokenSource"/> so disposing
///         the component while a load is in flight does not throw
///         <see cref="ObjectDisposedException"/> on torn-down fields.</item>
///   <item>Errors from the API surface in the error message bar.</item>
///   <item>Add failures do not clear the in-progress form values.</item>
/// </list>
/// Scaffolding mirrors <c>GradeLevelWizardTenancyTests</c>.
/// </summary>
[TestClass]
public class ContactsEditorTests : BunitContext
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string ErrorClass = "contacts-error";

    public ContactsEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    /// <summary>
    /// In-memory <see cref="IContactsClient"/> test double. Each method has a
    /// settable delegate so individual tests can swap success / failure /
    /// hang behavior without standing up an HTTP handler.
    /// </summary>
    private sealed class FakeContactsClient : IContactsClient
    {
        public List<ContactDto> Contacts { get; } = new();
        public List<SubscribedContactDto> Subscribed { get; } = new();

        public Func<Task<ContactDto[]?>>? OnListContacts { get; set; }
        public Func<AddContactRequest, Task<Guid>>? OnAddContact { get; set; }
        public Func<Guid, string, Task>? OnDeleteContact { get; set; }
        public Func<Guid, UpdateContactRequest, Task>? OnUpdateContact { get; set; }
        public Func<Guid, Task>? OnVerifyContact { get; set; }
        public Func<Task<SubscribedContactDto[]?>>? OnListSubscribed { get; set; }
        public Func<Guid, Task>? OnSubscribe { get; set; }
        public Func<Guid, Task>? OnUnsubscribe { get; set; }

        public int ListContactsCalls;
        public int AddContactCalls;
        public string? LastRequestedValue;
        public string? LastRequestedCountryCode;
        public int UpdateContactCalls;
        public Guid? LastUpdatedId;
        public UpdateContactRequest? LastUpdateRequest;
        public int DeleteContactCalls;
        public Guid? LastDeletedId;
        public string? LastDeleteReason;

        public Task<ContactDto[]?> ListContactsAsync(ContactOwnerType ownerType, Guid ownerId, CancellationToken ct = default)
        {
            ListContactsCalls++;
            if (OnListContacts is not null) return OnListContacts();
            return Task.FromResult<ContactDto[]?>(Contacts.ToArray());
        }

        public Task<Guid> AddContactAsync(AddContactRequest req, CancellationToken ct = default)
        {
            AddContactCalls++;
            LastRequestedValue = req.Value;
            LastRequestedCountryCode = req.CountryCode;
            if (OnAddContact is not null) return OnAddContact(req);
            var newId = Guid.NewGuid();
            Contacts.Add(new ContactDto(
                Id: newId,
                OwnerType: req.OwnerType,
                OwnerId: req.OwnerId,
                Channel: req.Channel,
                Value: req.Value,
                Label: req.Label,
                IsVerified: false,
                IsDeleted: false,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow) { CountryCode = req.CountryCode });
            return Task.FromResult(newId);
        }

        public Task UpdateContactAsync(Guid id, UpdateContactRequest req, CancellationToken ct = default)
        {
            UpdateContactCalls++;
            LastUpdatedId = id;
            LastUpdateRequest = req;
            if (OnUpdateContact is not null) return OnUpdateContact(id, req);
            return Task.CompletedTask;
        }

        public Task DeleteContactAsync(Guid id, string reason, CancellationToken ct = default)
        {
            DeleteContactCalls++;
            LastDeletedId = id;
            LastDeleteReason = reason;
            if (OnDeleteContact is not null) return OnDeleteContact(id, reason);
            Contacts.RemoveAll(c => c.Id == id);
            return Task.CompletedTask;
        }

        public Task<ContactAuditEntryDto[]?> ListContactAuditEntriesAsync(
            Guid? contactId = null,
            ContactOwnerType? ownerType = null,
            Guid? ownerId = null,
            int skip = 0,
            int take = 50,
            CancellationToken ct = default)
            => Task.FromResult<ContactAuditEntryDto[]?>([]);

        public Task VerifyContactAsync(Guid id, CancellationToken ct = default)
        {
            if (OnVerifyContact is not null) return OnVerifyContact(id);
            return Task.CompletedTask;
        }

        // Spec §4.9: contact display-order surface. The fake no-ops so
        // existing tests aren't affected; new tests can hook OnReorder /
        // OnSetOrder to assert behaviour.
        public Func<ContactOwnerType, Guid, IReadOnlyList<Guid>, Task>? OnReorder { get; set; }
        public Func<Guid, int, Task>? OnSetOrder { get; set; }

        public Task SetContactOrderAsync(Guid id, int order, CancellationToken ct = default)
        {
            if (OnSetOrder is not null) return OnSetOrder(id, order);
            return Task.CompletedTask;
        }

        public Task ReorderContactsAsync(ContactOwnerType ownerType, Guid ownerId, IReadOnlyList<Guid> orderedIds, CancellationToken ct = default)
        {
            if (OnReorder is not null) return OnReorder(ownerType, ownerId, orderedIds);
            return Task.CompletedTask;
        }

        public Task<SubscribedContactDto[]?> ListSubscribedContactsAsync(
            ContactOwnerType ownerType, Guid? ownerId = null, SubscriptionScope? scope = null, CancellationToken ct = default)
        {
            if (OnListSubscribed is not null) return OnListSubscribed();
            return Task.FromResult<SubscribedContactDto[]?>(Subscribed.ToArray());
        }

        public Task SubscribeAsync(
            Guid contactId, SubscriptionScope scope = SubscriptionScope.AllAssignments, Guid? scopeRefId = null, CancellationToken ct = default)
        {
            if (OnSubscribe is not null) return OnSubscribe(contactId);
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(
            Guid contactId, SubscriptionScope scope = SubscriptionScope.AllAssignments, Guid? scopeRefId = null, CancellationToken ct = default)
        {
            if (OnUnsubscribe is not null) return OnUnsubscribe(contactId);
            return Task.CompletedTask;
        }
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public HttpRequestMessage? LastRequest { get; private set; }

        public MockHttpMessageHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private static readonly string CountryCodesJson = JsonSerializer.Serialize(new[]
    {
        new CodedValueDto(FakeCodedValues.UsaId, "CNCODES_USA", "+1", null, null, null, false, 1, default, default,
            new[] { new CodedValueAttributeDto("COUNTRY", "United States") }, []),
        new CodedValueDto(FakeCodedValues.GhanaId, "CNCODES_GHA", "+233", null, null, null, false, 3, default, default,
            new[] { new CodedValueAttributeDto("COUNTRY", "Ghana") }, [])
    });

    private static class FakeCodedValues
    {
        public static readonly Guid GhanaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public static readonly Guid UsaId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    }

    private IRenderedComponent<ContactsEditor> RenderEditor(
        FakeContactsClient fake,
        ContactOwnerType ownerType = ContactOwnerType.Student,
        Guid? ownerId = null,
        bool showSubscription = true)
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, CountryCodesJson);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton<IContactsClient>(fake);
        Services.AddSingleton(new CodedValuesApiClient(http));
        Services.AddSingleton(NullLogger<ContactsEditor>.Instance);
        return Render<ContactsEditor>(parameters => parameters
            .Add(p => p.OwnerType, ownerType)
            .Add(p => p.OwnerId, ownerId ?? OwnerId)
            .Add(p => p.ShowSubscription, showSubscription));
    }

    [TestMethod]
    public void Dispose_CancelsInflightLoad()
    {
        // Arrange: a ListContactsAsync that blocks until we release the gate.
        // The component is rendered, then disposed before the load completes;
        // the cancellation token in LoadAsync should cancel the in-flight
        // request and prevent the load continuation from touching torn-down
        // state.
        var loadGate = new TaskCompletionSource<ContactDto[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeContactsClient
        {
            OnListContacts = () => loadGate.Task
        };

        var cut = RenderEditor(fake);

        // Act: dispose the component while the load is in flight. The
        // Cts.Cancel() inside Dispose should fire OperationCanceledException
        // in the load continuation, which the editor swallows.
        cut.Instance.Dispose();

        // Release the load; it should complete (or be cancelled) without
        // throwing on the disposed component.
        loadGate.TrySetResult(null);

        // Assert: the load was attempted exactly once (proves we hit LoadAsync).
        fake.ListContactsCalls.Should().Be(1, "the load was issued before disposal");
    }

    [TestMethod]
    public void LoadFailure_SurfacesErrorBar()
    {
        // Arrange: ListContactsAsync throws.
        var fake = new FakeContactsClient
        {
            OnListContacts = () => throw new InvalidOperationException("simulated load failure")
        };

        // Act
        var cut = RenderEditor(fake);

        // Assert: the error message bar renders with the exception text.
        cut.WaitForAssertion(() =>
        {
            cut.Find($".{ErrorClass}").TextContent.Should().Contain("simulated load failure");
        });
    }

    [TestMethod]
    public void AddAsync_Failure_DoesNotClearForm()
    {
        // Arrange: AddContactAsync throws. Pre-populate the form with a value
        // that should survive the failure.
        var fake = new FakeContactsClient
        {
            OnAddContact = _ => throw new InvalidOperationException("simulated add failure")
        };

        var cut = RenderEditor(fake);

        // Type a value into the new-contact text field. FluentTextField
        // renders as <fluent-text-field class="contacts-value"> — the
        // custom element itself carries the class, not the inner <input>.
        var valueInput = cut.Find("fluent-text-field.contacts-value");
        valueInput.Change("user@example.com");

        // Click the Add button. The fluent-button contains the "Add" text.
        var addButton = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Add"));
        addButton.Click();

        // Assert: the form is not cleared after the failure.
        cut.WaitForAssertion(() =>
        {
            var refreshed = cut.Find("fluent-text-field.contacts-value");
            refreshed.GetAttribute("value").Should().Be("user@example.com",
                "a failed Add must preserve the user's typed value");
        });
    }

    private void SetChannel(IRenderedComponent<ContactsEditor> cut, ContactChannel channel)
    {
        var field = typeof(ContactsEditor).GetField("_newChannel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(cut.Instance, channel);
        cut.Render();
    }

    private void SetCountryCodeSelection(IRenderedComponent<ContactsEditor> cut, Guid countryCodeId, CodedValueDto[] options)
    {
        var optionsField = typeof(ContactsEditor).GetField("_countryCodeOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var idField = typeof(ContactsEditor).GetField("_newCountryCodeId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        optionsField?.SetValue(cut.Instance, options);
        idField?.SetValue(cut.Instance, countryCodeId);
        cut.Render();
    }

    [TestMethod]
    public void EmailChannel_HidesCountryCodeDropdown()
    {
        var fake = new FakeContactsClient();
        var cut = RenderEditor(fake);

        cut.WaitForAssertion(() =>
        {
            // The country-code dropdown is a <CodedValueDropdown> whose
            // underlying <fluent-select> carries the `coded-value-dropdown`
            // base class (it used to carry a `contacts-country-code` width
            // class before the FieldWidth enum migration — see Width="W2"
            // on the call site). For the Email channel it is not rendered.
            cut.FindAll("fluent-select.coded-value-dropdown").Should().BeEmpty();
        });
    }

    [TestMethod]
    public void SmsChannel_ShowsCountryCodeDropdown()
    {
        var fake = new FakeContactsClient();
        var cut = RenderEditor(fake);

        SetChannel(cut, ContactChannel.SMS);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("fluent-select.coded-value-dropdown").Should().ContainSingle();
        });
    }

    [TestMethod]
    public void WhatsAppChannel_ShowsCountryCodeDropdown()
    {
        var fake = new FakeContactsClient();
        var cut = RenderEditor(fake);

        SetChannel(cut, ContactChannel.WhatsApp);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("fluent-select.coded-value-dropdown").Should().ContainSingle();
        });
    }

    [TestMethod]
    public void AddAsync_Sms_IncludesSelectedCountryCode()
    {
        var fake = new FakeContactsClient();
        var cut = RenderEditor(fake);

        SetChannel(cut, ContactChannel.SMS);
        SetCountryCodeSelection(cut, FakeCodedValues.GhanaId, new[]
        {
            new CodedValueDto(FakeCodedValues.UsaId, "CNCODES_USA", "+1", null, null, null, false, 1, default, default,
                new[] { new CodedValueAttributeDto("COUNTRY", "United States") }, []),
            new CodedValueDto(FakeCodedValues.GhanaId, "CNCODES_GHA", "+233", null, null, null, false, 3, default, default,
                new[] { new CodedValueAttributeDto("COUNTRY", "Ghana") }, [])
        });

        var valueInput = cut.Find("fluent-text-field.contacts-value");
        valueInput.Change("201234567");

        var addButton = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Add"));
        addButton.Click();

        cut.WaitForAssertion(() =>
        {
            fake.LastRequestedCountryCode.Should().Be("+233");
            fake.LastRequestedValue.Should().Be("201234567");
        });
    }

    [TestMethod]
    public void CountryCodeDropdown_OptionText_IncludesCountryName()
    {
        var fake = new FakeContactsClient();
        var cut = RenderEditor(fake);

        SetChannel(cut, ContactChannel.SMS);
        SetCountryCodeSelection(cut, FakeCodedValues.GhanaId, new[]
        {
            new CodedValueDto(FakeCodedValues.UsaId, "CNCODES_USA", "+1", null, null, null, false, 1, default, default,
                new[] { new CodedValueAttributeDto("COUNTRY", "United States") }, []),
            new CodedValueDto(FakeCodedValues.GhanaId, "CNCODES_GHA", "+233", null, null, null, false, 3, default, default,
                new[] { new CodedValueAttributeDto("COUNTRY", "Ghana") }, [])
        });

        var countryCodeDropdown = cut.FindComponent<CodedValueDropdown>();
        var optionText = countryCodeDropdown.Instance.OptionText;
        optionText.Should().NotBeNull("ContactsEditor should supply a display formatter that includes the country name");

        var ghana = new CodedValueDto(FakeCodedValues.GhanaId, "CNCODES_GHA", "+233", null, null, null, false, 3, default, default,
            new[] { new CodedValueAttributeDto("COUNTRY", "Ghana") }, []);
        var usa = new CodedValueDto(FakeCodedValues.UsaId, "CNCODES_USA", "+1", null, null, null, false, 1, default, default,
            new[] { new CodedValueAttributeDto("COUNTRY", "United States") }, []);

        optionText!(ghana).Should().Be("+233 (Ghana)");
        optionText!(usa).Should().Be("+1 (United States)");
    }

    // ─── Buffered mode (create-time, in-memory) ─────────────────────────
    // Spec §4.4 Option C: ContactsEditor gains a Buffered mode so the
    // picker can capture multiple contacts for a guardian that has not
    // been persisted yet. No API calls; the parent flushes on save.

    private IRenderedComponent<ContactsEditor> RenderBuffered(
        List<ContactModel>? contacts = null,
        ContactOwnerType ownerType = ContactOwnerType.Guardian)
    {
        contacts ??= new List<ContactModel>();
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, CountryCodesJson);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        // A fake that THROWs on AddContactAsync — Buffered mode must never
        // call the API. If it does, the test fails loudly.
        var fake = new FakeContactsClient
        {
            OnAddContact = _ => throw new InvalidOperationException("Buffered mode must not call AddContactAsync")
        };
        Services.AddSingleton<IContactsClient>(fake);
        Services.AddSingleton(new CodedValuesApiClient(http));
        Services.AddSingleton(NullLogger<ContactsEditor>.Instance);
        return Render<ContactsEditor>(parameters => parameters
            .Add(p => p.Mode, ContactsEditor.EditorMode.Buffered)
            .Add(p => p.OwnerType, ownerType)
            .Add(p => p.Contacts, contacts)
            .Add(p => p.ShowSubscription, false));
    }

    [TestMethod]
    public void BufferedMode_RendersEmptyState()
    {
        var contacts = new List<ContactModel>();
        var cut = RenderBuffered(contacts);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".contacts-empty").TextContent.Should().Contain("No contacts yet.");
        });
    }

    [TestMethod]
    public void BufferedMode_HidesSubscriptionToggle()
    {
        var contacts = new List<ContactModel>
        {
            new() { Channel = ContactChannel.Email, Value = "a@x.com", Order = 0 }
        };
        var cut = RenderBuffered(contacts);

        // Subscriptions need a persisted contact, so the subscribe toggle is
        // Live-only. The preferred contact is simply the first (lowest Order)
        // row; there is no per-contact Primary/CC role during creation.
        cut.FindAll(".contact-subscribe").Should().BeEmpty("subscribe toggle is Live-only");
    }

    [TestMethod]
    public void BufferedMode_Add_AppendsInMemory_WithoutApiCall()
    {
        var contacts = new List<ContactModel>();
        var cut = RenderBuffered(contacts);

        var valueInput = cut.Find("fluent-text-field.contacts-value");
        valueInput.Change("parent@example.com");

        var addButton = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Add"));
        addButton.Click();

        cut.WaitForAssertion(() =>
        {
            contacts.Should().HaveCount(1, "Buffered Add appends to the in-memory list");
            contacts[0].Value.Should().Be("parent@example.com");
            contacts[0].Channel.Should().Be(ContactChannel.Email);
            contacts[0].Order.Should().Be(0, "the first contact is Order 0 = preferred");
            // No error bar — the API was never called.
            cut.FindAll(".contacts-error").Should().BeEmpty();
        });
    }

    [TestMethod]
    public void BufferedMode_PreferredRow_ShowsPreferredBadgeAndHighlight()
    {
        var contacts = new List<ContactModel>
        {
            new() { Channel = ContactChannel.Email, Value = "preferred@x.com", Order = 0 },
            new() { Channel = ContactChannel.SMS, Value = "5551234", CountryCode = "+233", Order = 1 },
        };
        var cut = RenderBuffered(contacts);

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll(".contact-item");
            rows.Should().HaveCount(2);
            // The first (lowest Order) row is the preferred one: tinted
            // highlight + "Preferred" badge (not "Primary").
            rows[0].ClassList.Should().Contain("contact-item--preferred");
            rows[0].TextContent.Should().Contain("Preferred");
            rows[0].TextContent.Should().Contain("preferred@x.com");
            // The second row is not preferred and shows no Preferred badge.
            rows[1].ClassList.Should().NotContain("contact-item--preferred");
            rows[1].TextContent.Should().NotContain("Preferred");
        });
    }

    // ─── Edit / Remove with reason (spec 2026-08-17 §7.2) ───────────────
    // Both actions open the shared ContactChangeDialog via
    // DialogService.ShowShellDialogAsync. Live mode forwards the dialog's
    // reason to the API; Buffered mode mutates the in-memory list. The
    // dialog service is mocked (GradeNotificationPolicyEditorTests pattern)
    // so the reason-collection contract is exercised deterministically.

    private static ContactDto NewContact(Guid id, string value = "a@x.com")
        => new(id, ContactOwnerType.Student, OwnerId, ContactChannel.Email, value, "Home",
            IsVerified: false, IsDeleted: false, CreatedAt: default, UpdatedAt: default);

    private static IElement RowButton(IRenderedComponent<ContactsEditor> cut, string title)
        => cut.FindAll("fluent-button").First(b => b.GetAttribute("title") == title);

    /// <summary>
    /// Registers a mocked <see cref="IDialogService"/> that returns
    /// <paramref name="result"/> when <c>ContactChangeDialog</c> is opened.
    /// Returns the mock so tests can <c>Verify</c> the open occurred.
    /// </summary>
    private Mock<IDialogService> RegisterMockDialog(DialogResult result)
    {
        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(result));
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<ContactChangeDialog, DialogShellData<ContactChangeModel>>(
                It.IsAny<DialogShellData<ContactChangeModel>>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);
        Services.AddSingleton(dialogMock.Object);
        return dialogMock;
    }

    [TestMethod]
    public void LiveEdit_ClickingEdit_OpensContactChangeDialog()
    {
        var fake = new FakeContactsClient();
        fake.Contacts.Add(NewContact(Guid.NewGuid()));
        var dialogMock = RegisterMockDialog(DialogResult.Cancel());
        var cut = RenderEditor(fake);

        cut.WaitForAssertion(() => cut.FindAll(".contact-item").Should().HaveCount(1));
        RowButton(cut, "Edit contact").Click();

        cut.WaitForAssertion(() =>
            dialogMock.Verify(d => d.ShowDialogAsync<ContactChangeDialog, DialogShellData<ContactChangeModel>>(
                It.IsAny<DialogShellData<ContactChangeModel>>(), It.IsAny<DialogParameters>()), Times.Once));
    }

    [TestMethod]
    public void LiveDelete_ClickingDelete_OpensContactChangeDialog()
    {
        var fake = new FakeContactsClient();
        fake.Contacts.Add(NewContact(Guid.NewGuid()));
        var dialogMock = RegisterMockDialog(DialogResult.Cancel());
        var cut = RenderEditor(fake);

        cut.WaitForAssertion(() => cut.FindAll(".contact-item").Should().HaveCount(1));
        RowButton(cut, "Remove contact").Click();

        cut.WaitForAssertion(() =>
            dialogMock.Verify(d => d.ShowDialogAsync<ContactChangeDialog, DialogShellData<ContactChangeModel>>(
                It.IsAny<DialogShellData<ContactChangeModel>>(), It.IsAny<DialogParameters>()), Times.Once));
    }

    [TestMethod]
    public void LiveEdit_DialogReason_FlowsToUpdateContactAsync()
    {
        var contactId = Guid.NewGuid();
        var fake = new FakeContactsClient();
        fake.Contacts.Add(NewContact(contactId, "old@x.com"));
        var result = new ContactChangeResult(ContactChannel.Email, "new@x.com", "Home", null, "Parent requested change", IsDeleted: false);
        RegisterMockDialog(DialogResult.Ok(new DialogShellResult<ContactChangeResult>(result)));
        var cut = RenderEditor(fake);

        cut.WaitForAssertion(() => cut.FindAll(".contact-item").Should().HaveCount(1));
        RowButton(cut, "Edit contact").Click();

        cut.WaitForAssertion(() =>
        {
            fake.UpdateContactCalls.Should().Be(1, "the confirmed edit dialog must call the update API");
            fake.LastUpdatedId.Should().Be(contactId);
            fake.LastUpdateRequest!.Reason.Should().Be("Parent requested change",
                "the required dialog reason must flow to the update request");
            fake.LastUpdateRequest.Value.Should().Be("new@x.com");
        });
    }

    [TestMethod]
    public void LiveDelete_DialogReason_FlowsToDeleteContactAsync()
    {
        var contactId = Guid.NewGuid();
        var fake = new FakeContactsClient();
        fake.Contacts.Add(NewContact(contactId));
        var result = new ContactChangeResult(null, null, null, null, "Duplicate entry", IsDeleted: true);
        RegisterMockDialog(DialogResult.Ok(new DialogShellResult<ContactChangeResult>(result)));
        var cut = RenderEditor(fake);

        cut.WaitForAssertion(() => cut.FindAll(".contact-item").Should().HaveCount(1));
        RowButton(cut, "Remove contact").Click();

        cut.WaitForAssertion(() =>
        {
            fake.DeleteContactCalls.Should().Be(1, "the confirmed delete dialog must call the delete API");
            fake.LastDeletedId.Should().Be(contactId);
            fake.LastDeleteReason.Should().Be("Duplicate entry",
                "the required dialog reason must flow to the delete request");
        });
    }

    [TestMethod]
    public void BufferedEdit_InlineEdit_MutatesInMemoryList_WithoutApiCall()
    {
        var contacts = new List<ContactModel>
        {
            new() { Channel = ContactChannel.Email, Value = "old@x.com", Label = "Home", Order = 0 }
        };
        var targetId = contacts[0].TempId;
        var cut = RenderBuffered(contacts);

        cut.WaitForAssertion(() => cut.FindAll(".contact-item").Should().HaveCount(1));
        RowButton(cut, "Edit contact").Click();

        // Buffered mode opens an embedded SideDrawer with the edit form
        // (channel + value + label + Save/Cancel) inside the dialog content.
        cut.WaitForAssertion(() => cut.FindAll(".side-drawer-panel--embedded").Should().HaveCount(1));

        var valueField = cut.Find(".side-drawer-panel--embedded fluent-text-field.contacts-value");
        valueField.Change("new@x.com");

        var saveButton = cut.FindAll(".side-drawer-panel--embedded .side-drawer-btn-submit")
            .Cast<IElement>()
            .First();
        saveButton.Click();

        cut.WaitForAssertion(() =>
        {
            contacts[0].Value.Should().Be("new@x.com", "Buffered edit mutates the in-memory list");
            contacts[0].TempId.Should().Be(targetId, "the same row is edited (TempId preserved)");
            contacts[0].Label.Should().Be("Home", "Label is preserved");
        });

        var fake = (FakeContactsClient)Services.GetRequiredService<IContactsClient>();
        fake.UpdateContactCalls.Should().Be(0, "Buffered edit must not call the update API");
        fake.DeleteContactCalls.Should().Be(0);
        cut.FindAll(".contacts-error").Should().BeEmpty("no API call means no error bar");
    }

    [TestMethod]
    public void BufferedDelete_DialogResult_RemovesFromInMemoryList_WithoutApiCall()
    {
        var contacts = new List<ContactModel>
        {
            new() { Channel = ContactChannel.Email, Value = "a@x.com", Order = 0 },
            new() { Channel = ContactChannel.SMS, Value = "5551234", Order = 1 },
        };
        var removeId = contacts[0].TempId;
        var result = new ContactChangeResult(null, null, null, null, "Duplicate", IsDeleted: true);
        RegisterMockDialog(DialogResult.Ok(new DialogShellResult<ContactChangeResult>(result)));
        var cut = RenderBuffered(contacts);

        cut.WaitForAssertion(() => cut.FindAll(".contact-item").Should().HaveCount(2));
        RowButton(cut, "Remove contact").Click();

        cut.WaitForAssertion(() =>
        {
            contacts.Should().HaveCount(1, "Buffered delete removes the row from the in-memory list");
            contacts.Should().NotContain(c => c.TempId == removeId);
            contacts[0].Value.Should().Be("5551234", "the surviving row is the other contact");
        });

        var fake = (FakeContactsClient)Services.GetRequiredService<IContactsClient>();
        fake.DeleteContactCalls.Should().Be(0, "Buffered delete must not call the delete API");
        fake.UpdateContactCalls.Should().Be(0);
        cut.FindAll(".contacts-error").Should().BeEmpty("no API call means no error bar");
    }
}
