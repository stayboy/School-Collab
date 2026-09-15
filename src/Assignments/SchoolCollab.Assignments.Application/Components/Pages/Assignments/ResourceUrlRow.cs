namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>
/// WS-B2 (spec §3.4 / decision g) — one URL the author chose to attach as AI
/// reference material (shown in the Resources URL block, fed to the question
/// generator as a resource text, and mapped into the create request as a
/// <see cref="ResourceKindDto.Url"/> resource row). Plain data; non-empty +
/// dedupe validation live on <see cref="AssignmentEditFormModel.AddResourceUrl"/>.
/// </summary>
public sealed record ResourceUrlRow(string Url, string? DisplayName);
