using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Students.Application.Components.Pages.Teachers;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.Contracts;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the TeacherDetail page after the dm/2 contact migration:
/// the demographic review card renders, the Subjects / Grade levels cards
/// render, and the shared ContactsEditor is wired for
/// <see cref="ContactOwnerType.Teacher"/> (reversing the v1 staff
/// email/phone carve-out).
/// </summary>
[TestClass]
public class TeacherDetailPageBunitTests : BunitContext
{
    public TeacherDetailPageBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    /// <summary>In-memory <see cref="IContactsClient"/> that records the owner
    /// parameters it is asked to load, so the test can assert the TeacherDetail
    /// page wired the editor to ContactOwnerType.Teacher.</summary>
    private sealed class RecordingContactsClient : IContactsClient
    {
        public ContactOwnerType? LastOwnerType;
        public Guid? LastOwnerId;
        public int ListContactsCalls;

        public Task<ContactDto[]?> ListContactsAsync(ContactOwnerType ownerType, Guid ownerId, CancellationToken ct = default)
        {
            ListContactsCalls++;
            LastOwnerType = ownerType;
            LastOwnerId = ownerId;
            return Task.FromResult<ContactDto[]?>(Array.Empty<ContactDto>());
        }

        public Task<Guid> AddContactAsync(AddContactRequest req, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
        public Task UpdateContactAsync(Guid id, UpdateContactRequest req, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteContactAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task VerifyContactAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetContactOrderAsync(Guid id, int order, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReorderContactsAsync(ContactOwnerType ownerType, Guid ownerId, IReadOnlyList<Guid> orderedIds, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SubscribedContactDto[]?> ListSubscribedContactsAsync(ContactOwnerType ownerType, Guid? ownerId = null, SubscriptionScope? scope = null, CancellationToken ct = default) => Task.FromResult<SubscribedContactDto[]?>(Array.Empty<SubscribedContactDto>());
        public Task SubscribeAsync(Guid contactId, SubscriptionScope scope = SubscriptionScope.AllAssignments, Guid? scopeRefId = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task UnsubscribeAsync(Guid contactId, SubscriptionScope scope = SubscriptionScope.AllAssignments, Guid? scopeRefId = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url)> Calls = new();
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }
        public ScriptedHandler Map(string url, HttpStatusCode status, string body) => Map("ANY", url, status, body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery));
            var url = request.RequestUri.PathAndQuery;
            (HttpStatusCode Status, string Body)? found = null;
            if (_responses.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var exact))
                found = exact;
            else
            {
                foreach (var kv in _responses)
                {
                    if (kv.Key.Method != "ANY") continue;
                    if (url.Equals(kv.Key.Url, StringComparison.OrdinalIgnoreCase) ||
                        url.StartsWith(kv.Key.Url, StringComparison.OrdinalIgnoreCase))
                    {
                        found = kv.Value;
                        break;
                    }
                }
            }
            if (found is { } hit)
                return new HttpResponseMessage(hit.Status) { Content = new StringContent(hit.Body, Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {request.Method.Method} {url}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static ClaimsPrincipal CreateUser(bool realTenant)
    {
        var tenantId = realTenant ? Guid.NewGuid().ToString() : Guid.Empty.ToString();
        var claims = new[] { new Claim("tenant_id", tenantId), new Claim("tenant_name", realTenant ? "Hydeson" : "System") };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestScheme"));
    }

    private sealed class MutableAuthenticationStateProvider : AuthenticationStateProvider
    {
        private ClaimsPrincipal _user = new();
        public ClaimsPrincipal User { set { _user = value; NotifyAuthenticationStateChanged(GetAuthenticationStateAsync()); } }
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_user));
    }

    private static Dictionary<string, object?> TeacherJson(
        Guid id, Guid? titleId, string first, string last, DateOnly? dob = null) => new()
    {
        ["id"] = id,
        ["titleCodedValueId"] = titleId,
        ["firstName"] = first,
        ["lastName"] = last,
        ["displayName"] = "Mr. Ama",
        ["genderCodedValueId"] = (Guid?)null,
        ["dateOfBirth"] = dob,
        ["levelOfEducationCodedValueId"] = (Guid?)null,
        ["qualificationCodedValueIds"] = Array.Empty<Guid>(),
        ["isDeleted"] = false,
        ["createdAt"] = DateTimeOffset.UnixEpoch,
        ["updatedAt"] = DateTimeOffset.UnixEpoch,
    };

    private static Dictionary<string, object?> TopicJson(Guid id, string name, string code) => new()
    {
        ["id"] = id, ["codedValueId"] = (Guid?)null, ["code"] = code, ["name"] = name,
        ["description"] = (string?)null, ["displayOrder"] = 0,
        ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
    };

    private static Dictionary<string, object?> GradeLevelJson(Guid id, string name, int level) => new()
    {
        ["id"] = id, ["codedValueId"] = Guid.NewGuid(), ["level"] = level, ["name"] = name,
        ["displayOrder"] = level, ["topicCount"] = 0, ["studentCount"] = 0,
        ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
        ["minAge"] = (int?)null, ["maxAge"] = (int?)null,
        ["allowedGenderCodedValueId"] = (Guid?)null, ["isBlockedFromEnrollment"] = false,
    };

    private (ScriptedHandler Handler, RecordingContactsClient Contacts) Register(
        Guid teacherId, Guid? titleId = null, DateOnly? dob = null, string teacherStatus = "OK")
    {
        var auth = new MutableAuthenticationStateProvider { User = CreateUser(realTenant: true) };
        var handler = new ScriptedHandler();
        var status = Enum.Parse<HttpStatusCode>(teacherStatus);
        var body = teacherStatus == "OK"
            ? JsonSerializer.Serialize(TeacherJson(teacherId, titleId, "Ama", "Owusu", dob))
            : "";
        handler.Map("GET", $"/teachers/{teacherId}", status, body);
        handler.Map("GET", $"/teachers/{teacherId}/topics", HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { TopicJson(Guid.NewGuid(), "Mathematics", "MATH") }));
        handler.Map("GET", $"/teachers/{teacherId}/grade-levels", HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { GradeLevelJson(Guid.NewGuid(), "Grade 5", 5) }));
        if (titleId is { } tid)
            handler.Map("GET", $"/api/coded-values/{tid}", HttpStatusCode.OK, "{\"id\":\"" + tid + "\",\"name\":\"Mr.\"}");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));

        var contacts = new RecordingContactsClient();
        Services.AddSingleton<IContactsClient>(contacts);
        Services.AddSingleton(NullLogger<ContactsEditor>.Instance);

        return (handler, contacts);
    }

    [TestMethod]
    public void Detail_RendersDemographics_AndWiresTeacherContactsEditor()
    {
        var teacherId = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var dob = new DateOnly(1990, 4, 12);
        var (_, contacts) = Register(teacherId, titleId, dob);

        var cut = Render<TeacherDetail>(p => p.Add(x => x.Id, teacherId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ama"));
        cut.Markup.Should().Contain("Ama Owusu", "header shows full name");
        cut.Markup.Should().Contain("Mr.", "title coded-value name renders from GetByIdAsync");
        cut.Markup.Should().Contain("12 Apr 1990", "date of birth renders as dd MMM yyyy");
        cut.Markup.Should().Contain("Contacts");
        cut.Markup.Should().Contain("No contacts yet.");
        cut.Markup.Should().Contain("Grade levels (1)");
        cut.Markup.Should().Contain("Grade 5");

        // The shared ContactsEditor was rendered and asked to load contacts
        // for the teacher's owner id, i.e. ContactOwnerType.Teacher.
        cut.WaitForAssertion(() => contacts.ListContactsCalls.Should().Be(1));
        contacts.LastOwnerType.Should().Be(ContactOwnerType.Teacher);
        contacts.LastOwnerId.Should().Be(teacherId);
    }

    [TestMethod]
    public void Detail_TeacherNotFound_ShowsWarning()
    {
        var teacherId = Guid.NewGuid();
        Register(teacherId, teacherStatus: "NotFound");

        var cut = Render<TeacherDetail>(p => p.Add(x => x.Id, teacherId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Teacher not found."));
    }

    [TestMethod]
    public void Detail_NoSubjectsAndNoGradeLevels_ShowsEmptyState()
    {
        var teacherId = Guid.NewGuid();
        var (handler, _) = Register(teacherId);
        handler.Map("GET", $"/teachers/{teacherId}/topics", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/teachers/{teacherId}/grade-levels", HttpStatusCode.OK, "[]");

        var cut = Render<TeacherDetail>(p => p.Add(x => x.Id, teacherId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Contacts"));
        cut.Markup.Should().Contain("No contacts yet.");
        cut.Markup.Should().Contain("No grade levels linked.");
    }
}
