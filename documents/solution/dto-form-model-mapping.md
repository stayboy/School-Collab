# DTO → Form-Model Mapping (Lessons Learned)

**Concern: how a DTO (API projection) is converted into a UI form model.**

Enforced by `DotNetBestPracticesArchitectureTests.No_Inline_StudentFormModel_FieldCopies_In_Razor`
— see the companion doc [`dialog-parameter-binding.md`](./dialog-parameter-binding.md) for
the separate concern of how dialogs receive their inputs.

## Conversion belongs in a tested method on the form model, not the razor

### The bug

`StudentEditDialog` and the student Edit page both copied a `StudentDto`'s fields into a
`StudentFormModel` inline, field-by-field, in `OnInitializedAsync`:

```csharp
_model.StudentNumber = student.StudentNumber;
_model.FirstName = student.FirstName;
// ... 6 more lines
```

Duplicated across two call sites, untested, and easy to drift out of sync when either
type changes. A field added to the DTO but not the model (or vice-versa) showed up as a
blank field in the UI — with no compile error and no failing test.

### The rule

**Extract DTO → form-model projections into `From` / `LoadFrom` methods on the form model
itself**, and unit-test them. The form model must be a standalone public class (not a
nested class in a razor `@code` block) so the methods are reachable from call sites and
tests.

```csharp
// StudentFormModel.cs
public sealed class StudentFormModel
{
    public string? StudentNumber { get; set; }
    // ... other fields

    /// <summary>Project a DTO into a brand-new model (caller owns the instance).</summary>
    public static StudentFormModel From(StudentDto student)
    {
        var model = new StudentFormModel();
        model.LoadFrom(student);
        return model;
    }

    /// <summary>Populate this existing model from a DTO (the readonly-field-after-async-load case).</summary>
    public void LoadFrom(StudentDto student)
    {
        StudentNumber = student.StudentNumber;
        FirstName = student.FirstName;
        // ...
    }
}
```

Call sites become one line: `_model.LoadFrom(_student);` (populate existing) or
`StudentFormModel.From(dto)` (fresh model).

### Why two methods (`From` and `LoadFrom`)

- `From(dto)` returns a brand-new, fully-populated model — for callers that own the model
  (tests, or a page that swaps the whole model).
- `LoadFrom(dto)` mutates the existing instance — for the common case where the caller
  holds a `readonly StudentFormModel _model = new()` field and populates it after the
  async load.

Both share the single `LoadFrom` mapping body, so there is exactly one place the field
mapping lives.

### Why on the form model (not a separate `*Mappings` class)

An earlier version of this guide kept the projection in a separate static
`*FormModelMappings` extension-method class. We weighed both and moved to on-model
methods. The one argument that would have justified the separate class — that it keeps the
form model from taking a compile-time dependency on the wire DTO — **does not hold in this
codebase**: in every module the form model's project already references the DTO's assembly
(Students: the app-level `StudentDto` lives in the same `Students.Application` project;
Settings: `Settings.Application` already references `Admin.Shared` for the typed API
clients; Assignments: `Assignments.Application` already references `Assignments.Contracts`).
So a separate mapping class buys no real decoupling here.

On-model methods win on:

- **Discoverability.** Open the form model, see how it's populated — no separate
  `*Mappings` type to hunt for.
- **Fewer files.** The form-model file already exists; the projection folds into it and
  eliminates a per-module `*FormModelMappings.cs`.
- **Cohesion.** The mapping lives with the data it produces.

A separate mapping class *would* be the right call when the source type lives in a layer
the model's project does **not** already reference (a real boundary to protect) — not the
case for any current module.

### Prerequisite: extract the form model from the razor

If the form model is a `private`/nested class in a razor `@code` block, move it to its own
standalone public `.cs` file first (in the same namespace). On-model methods can't live on
a class that isn't independently reachable, and a presentational component shouldn't carry
mapping logic. This was done for all five form models (`StudentFormModel`,
`GradeLevelFormModel`, `CodedValueEditModel`, `EntityCodeRuleFormModel`,
`AssignmentEditFormModel`).

### Testing

Unit-test the mapping so a DTO/model field mismatch is caught by a test, not a blank UI
field:

- every profile field maps correctly (`LoadFrom`);
- `From` returns a fresh, fully-populated model with collection state empty;
- `LoadFrom` overwrites prior values (no stale state on reload).

## Adoption checklist

1. Make the form model a standalone public class (move it out of the razor `@code` if
   needed).
2. Put the DTO → form-model projection in `From` / `LoadFrom` methods on the model, not
   the razor and not a separate mapping class.
3. Test every field, the fresh-projection path, and the overwrite path.
4. Call sites use the on-model method (`_model.LoadFrom(dto)` / `Model.From(dto)`), never
   inline field-by-field assignment.
5. A NetArchTest guard fails CI if a razor file inline-copies a `StudentFormModel` field —
   so this is enforced for the Student module, not just recommended. (Other modules with
   their own form models should follow the same pattern; extend the guard as they adopt it.)