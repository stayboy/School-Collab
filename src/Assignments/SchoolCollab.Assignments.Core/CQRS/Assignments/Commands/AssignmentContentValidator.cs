using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands;

/// <summary>
/// Pure validation for inbound <see cref="NewContentModuleDto"/>,
/// <see cref="NewResourceDto"/>, and <see cref="NewAttachmentDto"/>
/// payloads on the create/update command surface (WS-A1). Mirrors
/// <see cref="QuestionOptionDtoValidator"/> per the ar-1 lesson: typed
/// <see cref="AssignmentContentValidationException"/> only, thrown BEFORE
/// any child is added to the aggregate so a partial state can never be
/// persisted. Shared by Create + Update handlers.
/// </summary>
internal static class AssignmentContentValidator
{
    /// <summary>Validate every inbound content module; throw on the first violation.</summary>
    public static void ValidateModules(IReadOnlyList<NewContentModuleDto>? modules)
    {
        if (modules is null || modules.Count == 0) return;
        for (var i = 0; i < modules.Count; i++)
        {
            ValidateModule(modules[i], listIndex: i);
        }
    }

    /// <summary>Validate every inbound AI-generation resource; throw on the first violation.</summary>
    public static void ValidateResources(IReadOnlyList<NewResourceDto>? resources)
    {
        if (resources is null || resources.Count == 0) return;
        for (var i = 0; i < resources.Count; i++)
        {
            ValidateResource(resources[i], listIndex: i);
        }
    }

    /// <summary>Validate every inbound attachment and enforce the
    /// <see cref="AttachmentUploadOptions.MaxTotalSizeBytes"/> cap as
    /// defense in depth on top of the per-file cap enforced at the
    /// staging endpoint.</summary>
    public static void ValidateAttachments(
        IReadOnlyList<NewAttachmentDto>? attachments,
        AttachmentUploadOptions options)
    {
        if (attachments is null || attachments.Count == 0) return;

        long total = 0;
        for (var i = 0; i < attachments.Count; i++)
        {
            var a = attachments[i];
            if (string.IsNullOrWhiteSpace(a.FileName))
                throw new AssignmentContentValidationException(
                    $"Attachments: entry at position {i} is missing FileName.");
            if (string.IsNullOrWhiteSpace(a.ContentType))
                throw new AssignmentContentValidationException(
                    $"Attachments: entry at position {i} is missing ContentType.");
            if (string.IsNullOrWhiteSpace(a.StoragePath))
                throw new AssignmentContentValidationException(
                    $"Attachments: entry at position {i} is missing StoragePath.");
            if (a.FileSize < 0)
                throw new AssignmentContentValidationException(
                    $"Attachments: entry at position {i} has a negative FileSize.");
            total += a.FileSize;
        }

        if (total > options.MaxTotalSizeBytes)
        {
            throw new AssignmentContentValidationException(
                $"Attachments: total size {total} bytes exceeds the {options.MaxTotalSizeBytes}-byte limit.");
        }
    }

    private static void ValidateModule(NewContentModuleDto m, int listIndex)
    {
        if (!Enum.IsDefined(typeof(ModuleType), (int)m.ModuleType))
        {
            throw new AssignmentContentValidationException(
                $"Module at position {listIndex}: unsupported module type '{m.ModuleType}'.");
        }

        if (string.IsNullOrWhiteSpace(m.Url))
        {
            throw new AssignmentContentValidationException(
                $"Module at position {listIndex}: Url is required.");
        }
        if (!IsAbsoluteHttpUrl(m.Url))
        {
            throw new AssignmentContentValidationException(
                $"Module at position {listIndex}: Url must be an absolute http/https URL.");
        }
        if (m.Url.Length > 2000)
        {
            throw new AssignmentContentValidationException(
                $"Module at position {listIndex}: Url exceeds 2000 characters.");
        }
        if (m.Title is { Length: > 200 })
        {
            throw new AssignmentContentValidationException(
                $"Module at position {listIndex}: Title exceeds 200 characters.");
        }
        if (m.MinCompletionThresholdPercent < 1 || m.MinCompletionThresholdPercent > 100)
        {
            throw new AssignmentContentValidationException(
                $"Module at position {listIndex}: MinCompletionThresholdPercent must be between 1 and 100.");
        }
    }

    private static void ValidateResource(NewResourceDto r, int listIndex)
    {
        if (!Enum.IsDefined(typeof(ResourceKind), (int)r.ResourceKind))
        {
            throw new AssignmentContentValidationException(
                $"Resource at position {listIndex}: unsupported resource kind '{r.ResourceKind}'.");
        }

        switch (r.ResourceKind)
        {
            case ResourceKindDto.Url:
                if (string.IsNullOrWhiteSpace(r.Url))
                {
                    throw new AssignmentContentValidationException(
                        $"Resource at position {listIndex}: kind Link requires Url.");
                }
                if (!IsAbsoluteHttpUrl(r.Url!))
                {
                    throw new AssignmentContentValidationException(
                        $"Resource at position {listIndex}: Url must be an absolute http/https URL.");
                }
                if (!string.IsNullOrEmpty(r.StoragePath))
                {
                    throw new AssignmentContentValidationException(
                        $"Resource at position {listIndex}: kind Link must not carry StoragePath.");
                }
                break;
            case ResourceKindDto.File:
                if (string.IsNullOrWhiteSpace(r.StoragePath))
                {
                    throw new AssignmentContentValidationException(
                        $"Resource at position {listIndex}: kind File requires StoragePath.");
                }
                if (!string.IsNullOrEmpty(r.Url))
                {
                    throw new AssignmentContentValidationException(
                        $"Resource at position {listIndex}: kind File must not carry Url.");
                }
                break;
            case ResourceKindDto.Video:
                var hasUrl = !string.IsNullOrWhiteSpace(r.Url);
                var hasStorage = !string.IsNullOrWhiteSpace(r.StoragePath);
                if (hasUrl == hasStorage)
                {
                    throw new AssignmentContentValidationException(
                        $"Resource at position {listIndex}: kind Video requires exactly one of Url or StoragePath.");
                }
                if (hasUrl && !IsAbsoluteHttpUrl(r.Url!))
                {
                    throw new AssignmentContentValidationException(
                        $"Resource at position {listIndex}: Url must be an absolute http/https URL.");
                }
                break;
        }

        if (r.DisplayName is { Length: > 200 })
        {
            throw new AssignmentContentValidationException(
                $"Resource at position {listIndex}: DisplayName exceeds 200 characters.");
        }
    }

    private static bool IsAbsoluteHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}
