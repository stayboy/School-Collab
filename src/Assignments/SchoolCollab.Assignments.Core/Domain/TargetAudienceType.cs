using System.ComponentModel;

namespace SchoolCollab.Assignments.Core.Domain;

public enum TargetAudienceType
{
    [Description("Everyone")]
    AllStudents = 0,
    [Description("By Grade Level")]
    SelectedGrades = 1,
    [Description("By Group")]
    SelectedGroups = 2
}