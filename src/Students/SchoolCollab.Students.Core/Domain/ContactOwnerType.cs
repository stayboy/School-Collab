namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// The entity that owns a <see cref="Contact"/> (spec §4.4).
/// </summary>
public enum ContactOwnerType
{
    Student = 0,
    Guardian = 1,
    Teacher = 2
}
