using System.ComponentModel;

namespace SchoolCollab.Assignments.Core.Domain;

public enum AssignmentType
{
    [Description("Fully online — MCQ, free-text, auto-graded")]
    Online = 0,

    [Description("Mix of online and physical submission")]
    Hybrid = 1,

    [Description("Instructions for physical submission")]
    Offline = 2
}