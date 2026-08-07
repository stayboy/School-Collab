namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Shared demographic base for people aggregates (<see cref="Student"/>,
/// <see cref="Guardian"/>, <see cref="Teacher"/>) — name, title, date of birth
/// and gender (grade-detail-rich-grids-plan.md §3). Holds only demographic
/// fields; each derived aggregate keeps its own identity, tenancy, audit and
/// role-specific state. EF maps the inherited demographic properties onto each
/// derived aggregate's own table (flattened, no shared table / discriminator).
/// </summary>
public abstract class PersonDemographic
{
    protected PersonDemographic() { }

    public Guid? TitleCodedValueId { get; protected set; }
    public string FirstName { get; protected set; } = default!;
    public string LastName { get; protected set; } = default!;
    public DateOnly? DateOfBirth { get; protected set; }
    public Guid? GenderCodedValueId { get; protected set; }

    /// <summary>
    /// Applies the shared demographic fields. Trims the name values.
    /// </summary>
    protected void SetDemographics(
        Guid? titleCodedValueId,
        string firstName,
        string lastName,
        DateOnly? dateOfBirth,
        Guid? genderCodedValueId)
    {
        TitleCodedValueId = titleCodedValueId;
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        DateOfBirth = dateOfBirth;
        GenderCodedValueId = genderCodedValueId;
    }
}
