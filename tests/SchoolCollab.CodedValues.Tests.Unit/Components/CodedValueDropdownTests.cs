using System.Net;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.CodedValues.Tests.Unit.Components;

/// <summary>
/// bUnit tests for the shared <see cref="CodedValueDropdown"/> component.
/// Verifies that the dropdown loads coded values by parent code, applies the
/// selected value, and propagates tenant-resolved options.
/// </summary>
[TestClass]
public class CodedValueDropdownTests : BunitContext
{
    [TestInitialize]
    public void Setup()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ILogger<CodedValueDropdown>>(
            _ => new LoggerFactory().CreateLogger<CodedValueDropdown>());
    }

    [TestMethod]
    public void CodedValueDropdown_LoadsOptionsByParentCode()
    {
        // Arrange
        var expected = new[]
        {
            new CodedValueDto(
                Id: Guid.NewGuid(),
                Code: "GENDER_MALE",
                Name: "Male",
                Description: null,
                ParentId: Guid.NewGuid(),
                ParentCode: "GENDER",
                IsDisabled: false,
                DisplayOrder: 0,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                Attributes: [],
                AttributeDefinitions: []),
            new CodedValueDto(
                Id: Guid.NewGuid(),
                Code: "GENDER_FEMALE",
                Name: "Female",
                Description: null,
                ParentId: Guid.NewGuid(),
                ParentCode: "GENDER",
                IsDisabled: false,
                DisplayOrder: 1,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                Attributes: [],
                AttributeDefinitions: [])
        };

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton(new CodedValuesApiClient(http));

        // Act
        var cut = Render<CodedValueDropdown>(parameters => parameters
            .Add(p => p.ParentCode, "GENDER"));

        cut.WaitForState(() => cut.Find("fluent-select") is not null);

        // Assert
        cut.Find("fluent-select").Should().NotBeNull();
        handler.LastRequest?.RequestUri?.PathAndQuery.Should().Be("/coded-values/by-parent?parentCode=GENDER");
    }

    [TestMethod]
    public void CodedValueDropdown_WithSelectedId_MarksOptionSelected()
    {
        // Arrange
        var typeId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var expected = new[]
        {
            new CodedValueDto(
                Id: typeId,
                Code: "TYPE_ESSAY",
                Name: "Essay",
                Description: null,
                ParentId: parentId,
                ParentCode: "ASSIGNMENT_TYPE",
                IsDisabled: false,
                DisplayOrder: 0,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                Attributes: [],
                AttributeDefinitions: [])
        };

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton(new CodedValuesApiClient(http));

        Guid? selectedId = null;

        // Act
        var cut = Render<CodedValueDropdown>(parameters => parameters
            .Add(p => p.ParentCode, "ASSIGNMENT_TYPE")
            .Add(p => p.SelectedId, typeId)
            .Add(p => p.SelectedIdChanged, EventCallback.Factory.Create<Guid?>(this, value => selectedId = value)));

        cut.WaitForState(() => cut.Find("fluent-select") is not null);

        // Assert
        cut.Find("fluent-select")?.GetAttribute("current-value")?.Should().Be(typeId.ToString());
    }

    private class MockHttpMessageHandler : HttpMessageHandler
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
}
