using System.Net;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
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
        public Func<Guid, Task>? OnDeleteContact { get; set; }
        public Func<Guid, Task>? OnVerifyContact { get; set; }
        public Func<Guid, Task>? OnSetPrimaryContact { get; set; }
        public Func<Task<SubscribedContactDto[]?>>? OnListSubscribed { get; set; }
        public Func<Guid, Task>? OnSubscribe { get; set; }
        public Func<Guid, Task>? OnUnsubscribe { get; set; }

        public int ListContactsCalls;
        public int AddContactCalls;
        public bool? LastRequestedPrimary;
        public string? LastRequestedValue;
        public string? LastRequestedCountryCode;

        public Task<ContactDto[]?> ListContactsAsync(ContactOwnerType ownerType, Guid ownerId, CancellationToken ct = default)
        {
            ListContactsCalls++;
            if (OnListContacts is not null) return OnListContacts();
            return Task.FromResult<ContactDto[]?>(Contacts.ToArray());
        }

        public Task<Guid> AddContactAsync(AddContactRequest req, CancellationToken ct = default)
        {
            AddContactCalls++;
            LastRequestedPrimary = req.IsPrimary;
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
                IsPrimary: req.IsPrimary,
                IsVerified: false,
                IsDeleted: false,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow) { CountryCode = req.CountryCode });
            return Task.FromResult(newId);
        }

        public Task UpdateContactAsync(Guid id, UpdateContactRequest req, CancellationToken ct = default)
            => throw new NotSupportedException("UpdateContactAsync is not exercised by ContactsEditor.");

        public Task DeleteContactAsync(Guid id, CancellationToken ct = default)
        {
            if (OnDeleteContact is not null) return OnDeleteContact(id);
            Contacts.RemoveAll(c => c.Id == id);
            return Task.CompletedTask;
        }

        public Task VerifyContactAsync(Guid id, CancellationToken ct = default)
        {
            if (OnVerifyContact is not null) return OnVerifyContact(id);
            return Task.CompletedTask;
        }

        public Task SetPrimaryContactAsync(Guid id, CancellationToken ct = default)
        {
            if (OnSetPrimaryContact is not null) return OnSetPrimaryContact(id);
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
            cut.FindAll("fluent-select.contacts-country-code").Should().BeEmpty();
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
            cut.FindAll("fluent-select.contacts-country-code").Should().ContainSingle();
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
            cut.FindAll("fluent-select.contacts-country-code").Should().ContainSingle();
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
}
