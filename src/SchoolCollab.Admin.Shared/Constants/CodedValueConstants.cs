namespace SchoolCollab.Admin.Shared.Constants;

public enum CodedValueParent
{
    Genders = 0,
    Status = 1,
    AiModels = 2,
    Subjects = 3,
    Grades = 4
}

public static class CodedValueParentExtensions
{
    public static string ToCode(this CodedValueParent parent) => parent switch
    {
        CodedValueParent.Genders => "GENDERS",
        CodedValueParent.Status => "STATUS",
        CodedValueParent.AiModels => "AI-MODELS",
        CodedValueParent.Subjects => "SUBJECTS",
        CodedValueParent.Grades => "GRADES",
        _ => throw new ArgumentOutOfRangeException(nameof(parent))
    };
}
