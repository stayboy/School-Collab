namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// File-store configuration for assignment uploads (WS-A1 / D-1: local FS dev
/// implementation; Azure Blob deferred). Bound in
/// <c>AddAssignmentsCore</c> from the <see cref="SectionName"/> section.
/// </summary>
public sealed class AssignmentFileStoreOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Assignments:FileStore";

    /// <summary>The local root directory for staged files. Relative
    /// paths are resolved against <see cref="System.AppContext.BaseDirectory"/>
    /// at construction time (see <c>LocalFileStore</c>).</summary>
    public string RootPath { get; set; } = "assignment-files";
}
