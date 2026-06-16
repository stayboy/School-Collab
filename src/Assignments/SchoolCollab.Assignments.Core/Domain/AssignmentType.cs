using System.ComponentModel;

namespace SchoolCollab.Assignments.Core.Domain;

public enum AssignmentType
{
    [Description("Online")]
    Digital = 0,
    [Description("Hybrid")]
    SemiManual = 1,
    [Description("Offline")]
    Manual = 2
}