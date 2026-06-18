using System.ComponentModel;

namespace SchoolCollab.Assignments.Core.Domain;

public enum GradingFormat
{
    [Description("Teacher Marked")]
    TeacherGraded = 0,
    [Description("Auto Scored")]
    AutoGraded = 1,
    [Description("Instant Feedback")]
    InstantGraded = 2
}