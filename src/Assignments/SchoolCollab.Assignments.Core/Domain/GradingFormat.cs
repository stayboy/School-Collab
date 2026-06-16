using System.ComponentModel;

namespace SchoolCollab.Assignments.Core.Domain;

public enum GradingFormat
{
    [Description("Auto-graded — System scores MCQ/fixed-answer automatically")]
    AutoGraded = 0,

    [Description("Instant-graded — Immediate feedback on submission")]
    InstantGraded = 1,

    [Description("Teacher-graded — Teacher reviews and scores manually")]
    TeacherGraded = 2
}

public enum TargetAudience
{
    [Description("All students — Available to everyone in the grade/subject")]
    AllStudents = 0,

    [Description("Selected students — Choose specific students")]
    SelectedStudents = 1,

    [Description("Selected grades/classes — Choose specific classes")]
    SelectedGrades = 2
}