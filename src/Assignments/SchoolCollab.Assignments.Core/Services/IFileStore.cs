namespace SchoolCollab.Assignments.Core.Services;

/// <summary>File-storage port for assignment uploads (WS-A1 / D-1: local FS dev
/// implementation; Azure Blob deferred). <see cref="StoreAsync"/> returns an
/// opaque <c>storagePath</c> string that callers persist on their entities
/// (e.g. <c>AssignmentAttachment.StoragePath</c>,
/// <c>AssignmentResource.StoragePath</c>,
/// <c>ContentModule.StoragePath</c>); the value is meaningful only inside
/// the implementing store and must be treated as opaque by every consumer.</summary>
public interface IFileStore
{
    /// <summary>Persists <paramref name="content"/> under a fresh opaque
    /// storage path and returns that path. The implementation owns the
    /// path layout (tenant scoping, staging prefix, file-name sanitization)
    /// and the on-disk naming.</summary>
    Task<string> StoreAsync(Stream content, string fileName, string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a readable stream for the file at the given
    /// <paramref name="storagePath"/>. Throws
    /// <see cref="FileNotFoundException"/> when the file is missing.</summary>
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <summary>Deletes the file at the given <paramref name="storagePath"/>.
    /// Idempotent — a missing file is a no-op rather than an error so the
    /// orphan sweep can re-delete without race conditions.</summary>
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
}
