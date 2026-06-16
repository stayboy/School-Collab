namespace SchoolCollab.Assignments.Core.Domain;

public sealed class AssignmentAttachment
{
    private AssignmentAttachment() { }

    internal AssignmentAttachment(Guid assignmentId, string fileName, string contentType, long fileSize, string storagePath)
    {
        Id = Guid.NewGuid();
        AssignmentId = assignmentId;
        FileName = fileName;
        ContentType = contentType;
        FileSize = fileSize;
        StoragePath = storagePath;
    }

    public Guid Id { get; private set; }
    public Guid AssignmentId { get; private set; }
    public string FileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long FileSize { get; private set; }
    public string StoragePath { get; private set; } = default!;
}