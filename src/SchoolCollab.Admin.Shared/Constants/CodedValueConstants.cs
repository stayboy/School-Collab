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
        CodedValueParent.Genders => "GENDER",
        CodedValueParent.Status => "STATUS",
        CodedValueParent.AiModels => "AI-MODELS",
        CodedValueParent.Subjects => "SUBJECT",
        CodedValueParent.Grades => "GRADE",
        _ => throw new ArgumentOutOfRangeException(nameof(parent))
    };
}
