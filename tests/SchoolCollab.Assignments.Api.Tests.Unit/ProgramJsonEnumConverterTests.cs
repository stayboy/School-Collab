using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// WS-E2b / ar-18 — guards the REAL host's JSON enum registrations for the two delivery
/// enums on the failures read side (<see cref="NotificationKindDto"/> /
/// <see cref="ContactChannelDto"/>), which were the only pair of the assignment enums
/// missing from Program.cs.
///
/// Why the first test is a source scan: every other test in this project builds its own
/// minimal TestServer and registers the enum converters itself
/// (<c>SignOffRoutesTests</c> / <c>GuardianTokenEndpointFilterTests</c> /
/// <c>WardRoutesTests</c>), so none of those hosts can observe Program.cs at all — which
/// is exactly how the ar-15 gap hid until its converters were moved into Program.cs. No
/// harness boots the real host here either: Program.cs resolves Postgres, RabbitMQ, Redis
/// and the Settings overlay, none of which exist in unit-test CI. The scan is therefore
/// the discriminating half — it fails if either registration leaves the host's
/// <c>ConfigureHttpJsonOptions</c> block — while the second test pins the resulting wire
/// shape (enum NAMES, not numbers).
/// </summary>
[TestClass]
public class ProgramJsonEnumConverterTests
{
    /// <summary>Same repo-root discovery as <c>SchoolCollab.ArchitectureTests.Unit</c>:
    /// <c>tests/&lt;Project&gt;/bin/&lt;Configuration&gt;/&lt;Tfm&gt;</c> sits five levels below the
    /// repository root.</summary>
    private static string ProgramSourcePath()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            assemblyDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(
            repositoryRoot, "src", "Assignments", "SchoolCollab.Assignments.Api", "Program.cs");
    }

    [TestMethod]
    public void HostOptions_RegisterBothDeliveryEnumsAsStrings()
    {
        var programPath = ProgramSourcePath();
        File.Exists(programPath).Should().BeTrue(
            $"the host source must be locatable from the test assembly (looked for {programPath})");

        var source = File.ReadAllText(programPath);
        var blockStart = source.IndexOf("ConfigureHttpJsonOptions(", StringComparison.Ordinal);
        blockStart.Should().BeGreaterThan(-1, "Program.cs must configure the HTTP JSON options");
        var blockEnd = source.IndexOf("\n});", blockStart, StringComparison.Ordinal);
        blockEnd.Should().BeGreaterThan(blockStart, "the options block must be terminated");
        var optionsBlock = source[blockStart..blockEnd];

        optionsBlock.Should().Contain("JsonStringEnumConverter<NotificationKindDto>",
            "the failures read side serializes NotificationKindDto — without this registration the live wire form is a number");
        optionsBlock.Should().Contain("JsonStringEnumConverter<ContactChannelDto>",
            "the failures read side serializes ContactChannelDto — without this registration the live wire form is a number");
    }

    [TestMethod]
    public void HostOptions_SerializeDeliveryEnumsAsNames_NotNumbers()
    {
        // Mirrors the host's options for the two delivery enums (Web defaults + both
        // converters), so this pins the wire form the live host now produces.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter<NotificationKindDto>(),
                new JsonStringEnumConverter<ContactChannelDto>(),
            }
        };

        var json = JsonSerializer.Serialize(
            new NotificationFailureDto(
                RecipientId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                ContactId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Channel: ContactChannelDto.Email,
                Kind: NotificationKindDto.Publish,
                Attempt: 3,
                FailureReason: "Provider replied: 550 permanent failure",
                NextRetryAt: null),
            options);

        json.Should().Contain("\"kind\":\"Publish\"", "the kind travels as its enum name");
        json.Should().Contain("\"channel\":\"Email\"", "the channel travels as its enum name");
        json.Should().NotMatchRegex("\"kind\":\\s*\\d",
            "a numeric kind is the pre-ar-18 live shape");
        json.Should().NotMatchRegex("\"channel\":\\s*\\d",
            "a numeric channel is the pre-ar-18 live shape");
    }
}
