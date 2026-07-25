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
    CountryCallingCodes = 10
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
        _ => throw new ArgumentOutOfRangeException(nameof(parent))
    };
}
