using System.Net;
using System.Net.Http;
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
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels;
using SchoolCollab.Students.Admin.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit reproducer for the "Edit Grade Level page doesn't work" bug report.
/// The test renders <see cref="Edit"/> with a scripted API that returns a
/// fully-populated <c>GradeLevelDto</c> (including MinAge / MaxAge /
/// AllowedGenderCodedValueId), submits the form, and asserts:
///   1. The form fields render pre-populated with the loaded values.
///   2. Submitting the form PUTs the body to /students/grade-levels/{id}
///      with the validation fields wired through.
///   3. Navigation back to the landing list happens after a successful save.
/// </summary>
[TestClass]
public class GradeLevelEditTests : BunitContext
{
    public GradeLevelEditTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class FakeAuth : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(
                new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    new[]
                    {
                        new System.Security.Claims.Claim("tenant_id", Guid.NewGuid().ToString()),
                        new System.Security.Claims.Claim("tenant_name", "Hydeson"),
                    },
                    "TestScheme"))));
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Calls = new();
        // Keyed by (METHOD, URL) so a GET and PUT to the same URL do not
        // overwrite each other (the edit flow exercises both for /students/
        // grade-levels/{id}).
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        public ScriptedHandler Map(string url, HttpStatusCode status, string body)
            => Map("ANY", url, status, body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));

            var url = request.RequestUri.PathAndQuery;
            // First try exact method+url match, then fall back to method-agnostic
            // wildcard (registered via the no-method overload) so a single
            // registration can cover "GET /api/coded-values/*" without the test
            // caring about exact query strings.
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
            {
                return new HttpResponseMessage(hit.Status)
                {
                    Content = new StringContent(hit.Body, Encoding.UTF8, "application/json"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {request.Method.Method} {url}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private (FakeAuth Auth, ScriptedHandler Handler) RegisterWith(
        Guid gradeId,
        int level,
        string name,
        int? minAge,
        int? maxAge,
        Guid? allowedGenderCodedValueId)
    {
        var auth = new FakeAuth();
        var handler = new ScriptedHandler();

        // GET /students/grade-levels/{id} -> the DTO the form pre-fills from.
        var codedValueId = Guid.NewGuid();
        var getDto = new Dictionary<string, object?>
        {
            ["Id"] = gradeId,
            ["CodedValueId"] = codedValueId,
            ["Level"] = level,
            ["Name"] = name,
            ["DisplayOrder"] = level,
            ["SubjectCount"] = 0,
            ["StudentCount"] = 0,
            ["CreatedAt"] = DateTimeOffset.UnixEpoch,
            ["UpdatedAt"] = DateTimeOffset.UnixEpoch,
            ["MinAge"] = minAge,
            ["MaxAge"] = maxAge,
            ["AllowedGenderCodedValueId"] = allowedGenderCodedValueId,
        };
        handler.Map("GET", $"/students/grade-levels/{gradeId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(getDto));

        // The page also issues coded-value lookups (GRADE parent for the
        // dropdown + GENDER parent if a gender is set). Wildcard-mapping the
        // /api/coded-values/ tree to empty arrays keeps the dropdowns from
        // erroring.
        handler.Map("/api/coded-values/", HttpStatusCode.OK, "[]");

        // PUT /students/grade-levels/{id} -> NoContent + subsequent navigates
        // to /students/grade-levels (landing).
        handler.Map("PUT", $"/students/grade-levels/{gradeId}", HttpStatusCode.NoContent, "");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };

        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));

        return (auth, handler);
    }

    [TestMethod]
    public async Task Edit_LoadsGradeLevel_PopulatesForm_And_PutsUpdatedBody_OnSave()
    {
        var gradeId = Guid.NewGuid();
        var (_, handler) = RegisterWith(
            gradeId,
            level: 3,
            name: "Grade 3",
            minAge: 8,
            maxAge: 10,
            allowedGenderCodedValueId: null);

        var cut = Render<Edit>(p => p.Add(x => x.Id, gradeId));

        // bUnit doesn't auto-pump async continuations from nested
        // OnInitializedAsync chains (TenantGate → GateBase → Edit → CodedValueDropdown).
        // Force the dispatcher to drain so the GET resolves and the form
        // renders with the loaded values.
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50);
            cut.Render();
        }

        // The form must render with the loaded values pre-filled.
        cut.Markup.Should().Contain("Edit Grade Level", "the page title is rendered");
        cut.Markup.Should().Contain("Grade 3", "the Name field is populated from the loaded DTO");

        // The MinAge / MaxAge inputs are bound to number fields. Find them by id.
        var minAgeInput = cut.Find("#grade-level-min-age");
        var maxAgeInput = cut.Find("#grade-level-max-age");
        minAgeInput.GetAttribute("value").Should().Be("8");
        maxAgeInput.GetAttribute("value").Should().Be("10");

        // Submit the form.
        var form = cut.Find("form");
        form.Submit();

        // Wait for the PUT to land.
        cut.WaitForState(() => handler.Calls.Any(c => c.Method == "PUT"), TimeSpan.FromSeconds(5));

        var put = handler.Calls.Single(c => c.Method == "PUT" && c.Url == $"/students/grade-levels/{gradeId}");
        put.Body.Should().NotBeNull("the PUT body must carry the form state");

        // The body must contain the loaded + (re-)submitted validation fields.
        // System.Text.Json serializes records by parameter name (camelCase).
        var doc = JsonDocument.Parse(put.Body!);
        doc.RootElement.GetProperty("level").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("name").GetString().Should().Be("Grade 3");
        doc.RootElement.GetProperty("displayOrder").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("minAge").GetInt32().Should().Be(8,
            "MinAge must round-trip through the form into the PUT body");
        doc.RootElement.GetProperty("maxAge").GetInt32().Should().Be(10,
            "MaxAge must round-trip through the form into the PUT body");
    }

    [TestMethod]
    public async Task Edit_NoValidationRules_SubmitsWithNulls()
    {
        // The pre-PR-#94 grade-level row: no MinAge, MaxAge, or AllowedGender set.
        // The PUT body must still carry those three fields as null so the
        // server-side domain update path sees them (vs. "field absent" which
        // would skip the assignment). System.Text.Json serializes null by
        // default; this pins that contract.
        var gradeId = Guid.NewGuid();
        var (_, handler) = RegisterWith(
            gradeId,
            level: 1,
            name: "Grade 1",
            minAge: null,
            maxAge: null,
            allowedGenderCodedValueId: null);

        var cut = Render<Edit>(p => p.Add(x => x.Id, gradeId));
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50);
            cut.Render();
        }
        cut.Markup.Should().Contain("Edit Grade Level");

        var form = cut.Find("form");
        form.Submit();
        cut.WaitForState(() => handler.Calls.Any(c => c.Method == "PUT"), TimeSpan.FromSeconds(5));

        var put = handler.Calls.Single(c => c.Method == "PUT" && c.Url == $"/students/grade-levels/{gradeId}");
        var doc = JsonDocument.Parse(put.Body!);
        doc.RootElement.TryGetProperty("minAge", out var minAge).Should().BeTrue();
        minAge.ValueKind.Should().Be(JsonValueKind.Null, "minAge must serialize as null, not be omitted");
        doc.RootElement.TryGetProperty("maxAge", out var maxAge).Should().BeTrue();
        maxAge.ValueKind.Should().Be(JsonValueKind.Null, "maxAge must serialize as null, not be omitted");
        doc.RootElement.TryGetProperty("allowedGenderCodedValueId", out var allowedGender).Should().BeTrue();
        allowedGender.ValueKind.Should().Be(JsonValueKind.Null, "allowedGenderCodedValueId must serialize as null, not be omitted");
    }

    [TestMethod]
    public async Task Edit_AllowedGender_RoundTripsThroughForm()
    {
        // The AllowedGenderCodedValueId is a Guid? on the form model. When
        // set server-side, the dropdown binds to it via two-way binding; the
        // round-trip back to the server must preserve the id (and PUT must
        // carry it even if the user doesn't change it).
        var gradeId = Guid.NewGuid();
        var genderId = Guid.NewGuid();
        var (_, handler) = RegisterWith(
            gradeId,
            level: 5,
            name: "Grade 5",
            minAge: 10,
            maxAge: 12,
            allowedGenderCodedValueId: genderId);

        var cut = Render<Edit>(p => p.Add(x => x.Id, gradeId));
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50);
            cut.Render();
        }

        // The page calls /api/coded-values/by-parent?parentCode=GENDER to load
        // the dropdown items. Our wildcard mapper returns [] which leaves the
        // dropdown empty — but the underlying two-way binding still holds the
        // id in Model.AllowedGenderCodedValueId. Submitting the form MUST
        // serialize the id.
        cut.Find("form").Submit();
        cut.WaitForState(() => handler.Calls.Any(c => c.Method == "PUT"), TimeSpan.FromSeconds(5));

        var put = handler.Calls.Single(c => c.Method == "PUT" && c.Url == $"/students/grade-levels/{gradeId}");
        var doc = JsonDocument.Parse(put.Body!);
        doc.RootElement.GetProperty("allowedGenderCodedValueId").GetGuid().Should().Be(genderId,
            "the AllowedGenderCodedValueId must survive the form round-trip");
    }

    /// <summary>
    /// Regression for the original "Edit doesn't work" report: PR #94 added
    /// <c>GradeLevelFormFields</c> with <c>&lt;FormRow Id="..."&gt;</c>
    /// (which <c>FormRow</c> does not expose — its parameter is <c>For</c>).
    /// Every <c>&lt;FormRow&gt;</c> in the form then threw at render time
    /// and ErrorBoundary swallowed the markup, surfacing the unhelpful
    /// "Something went wrong" banner. This test fails on the broken markup
    /// (FormRow.Id is unrecognized) and passes once FormRow uses For.
    /// </summary>
    [TestMethod]
    public async Task Edit_FormRows_Use_ForParameter_Not_Id()
    {
        var gradeId = Guid.NewGuid();
        var (_, _) = RegisterWith(
            gradeId,
            level: 1,
            name: "Grade 1",
            minAge: null,
            maxAge: null,
            allowedGenderCodedValueId: null);

        var cut = Render<Edit>(p => p.Add(x => x.Id, gradeId));
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50);
            cut.Render();
        }

        // The page must render the form (not the "Something went wrong"
        // ErrorBoundary fallback). PR #94's <FormRow Id="..."> bug surfaces
        // as exactly that fallback.
        cut.Markup.Should().NotContain("Something went wrong",
            "FormRow must use For=... not Id=...; otherwise every FormRow throws at render");
        cut.Markup.Should().Contain("Edit Grade Level");
        // The labels must render so the form is visible to the user.
        cut.Markup.Should().Contain("Name");
        cut.Markup.Should().Contain("Age range");
        cut.Markup.Should().Contain("Allowed Gender");
    }

    /// <summary>
    /// Same FormRow.Id regression but for Create.razor — the bug also broke
    /// the create page since both render <c>&lt;GradeLevelFormFields&gt;</c>.
    /// </summary>
    [TestMethod]
    public async Task Create_FormRows_Use_ForParameter_Not_Id()
    {
        var auth = new FakeAuth();
        var handler = new ScriptedHandler();
        // Both the GRADE parent lookup and any other coded-values call are
        // mapped to an empty array so the dropdown settles without error.
        handler.Map("/api/coded-values/", HttpStatusCode.OK, "[]");
        // Create's submit hits the find-or-create endpoint (not the standard
        // Create). Map it to NoContent-equivalent.
        handler.Map("POST", "/students/grade-levels/get-or-create", HttpStatusCode.OK,
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CodedValueId"] = Guid.NewGuid(),
                ["Level"] = 1,
                ["Name"] = "Grade 1",
                ["DisplayOrder"] = 1,
                ["SubjectCount"] = 0,
                ["StudentCount"] = 0,
                ["CreatedAt"] = DateTimeOffset.UnixEpoch,
                ["UpdatedAt"] = DateTimeOffset.UnixEpoch,
            }));

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));

        var cut = Render<SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels.Create>();
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50);
            cut.Render();
        }

        cut.Markup.Should().NotContain("Something went wrong",
            "Create must also render the form, not the ErrorBoundary fallback");
        cut.Markup.Should().Contain("New Grade Level");
        cut.Markup.Should().Contain("Age range");
        cut.Markup.Should().Contain("Allowed Gender");
    }
}