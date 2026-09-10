namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// Kind of student-facing content module on an assignment (WS-A1 /
/// spec §4.10). <c>Video</c> is the streamed-link / embedded player case;
/// <c>Guide</c> is the structured reading / printable-materials case.
/// </summary>
public enum ModuleType
{
    Video = 0,
    Guide = 1
}
