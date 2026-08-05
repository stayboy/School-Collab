namespace SchoolCollab.Admin.Shared.Constants;

public enum CodedValueParent
{
    Genders = 0,
    Status = 1,
    AiModels = 2,
    Subjects = 3,
    Grades = 4,
    Relationships = 5,
    Salutations = 6,
    Communities = 7,
    Cities = 8,
    Countries = 9,
    CountryCallingCodes = 10,
    /// <summary>Grade strands (children of <c>GRSTRNDS</c>). Each strand
    /// has a <c>gradeLevel</c> attribute referencing its parent grade's
    /// coded value. Picker filters by attribute when a grade is selected.</summary>
    GradeStrands = 11,
    /// <summary>Teacher roles on a grade link (children of <c>TCHROLES</c>).
    /// Nullable FK on <c>TeacherGradeLevel.TeacherRoleCodedValueId</c>.</summary>
    TeacherRoles = 12
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
        CodedValueParent.Relationships => "RELATSHIPS",
        CodedValueParent.Salutations => "SALUTS",
        CodedValueParent.Communities => "COMMUNITYS",
        CodedValueParent.Cities => "CITIES",
        CodedValueParent.Countries => "COUNTRYS",
        CodedValueParent.CountryCallingCodes => "CNCODES",
        CodedValueParent.GradeStrands => "GRSTRNDS",
        CodedValueParent.TeacherRoles => "TCHROLES",
        _ => throw new ArgumentOutOfRangeException(nameof(parent))
    };
}
