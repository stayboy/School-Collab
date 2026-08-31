# Runbook — FR-58 publish-time topic-assignment validation

> Owner: Assignments / Students cross-context integration
> Related specs: `documents/specs/activity-group-enrollment.md` (FR-55..58),
> `documents/specs/subject-to-topic-polymorphism.md` (bridge model)
> Last updated: 2026-08-27

## What FR-58 does

When an assignment is published, `PublishAssignmentCommandHandler` calls
`ITopicAssignmentLookup.IsTopicAssignedAsync(...)` to verify that the selected
subject/topic is actually assigned to the target audience **for the effective
period**.

- `SelectedGrades`: checks `GET /students/topic-assignments/by-grade/{gradeId}?effectiveDate=...`
- `SelectedGroups`: checks `GET /students/topic-assignments/by-activity-group/{groupId}?effectiveDate=...`
- `AllStudents`: **not checked** by FR-58 (see scope note below)

Effective date order: `assignment.DueDate ?? assignment.PublishedAt ?? DateTimeOffset.UtcNow`.

## Fail-open behavior

If the Students API is unreachable when publishing, the HTTP lookup catches
`HttpRequestException` and **returns `true`** so the publish proceeds.

Log warning emitted:

```text
Students API unreachable checking grade topic assignment; failing open
Students API unreachable checking group topic assignment; failing open
```

Source:
`src/Assignments/SchoolCollab.Assignments.Api/Services/TopicAssignmentLookupHttpClient.cs`

### Why fail-open?

This check is a *publish-time validation refinement*, not a hard gate. If the
Students API is down, blocking every publish would be a larger availability risk
than allowing a potentially mismatched assignment to be published. The trade-off
was accepted during implementation review (see
`documents/rounds/review-phases-completed.md` §4 item 4).

### Risks and mitigation

- A topic could be published against a grade/group that does not have it
  assigned, but only while the Students API is unreachable.
- Recovery: when the Students API recovers, administrators can inspect published
  assignments and bridge assignments via the Students / Admin UI.
- Monitoring: alert on the warning log above; correlate with Students API
  availability metrics.

## How to change to fail-closed

If the product later decides that publish must be blocked when the Students API
is unreachable, change `TopicAssignmentLookupHttpClient` so that the
`HttpRequestException` handler returns `false` **or** throws a custom exception
that `PublishAssignmentCommandHandler` maps to a 422/503. The current handler
returns `true` to preserve the fail-open contract.

## `AllStudents` scope note

FR-58 intentionally validates only `SelectedGrades` and `SelectedGroups`. An
`AllStudents` assignment has no single grade or group to check against the
bridge, so the lookup short-circuits to `true`. Whether `AllStudents` should
require the topic to be assigned to *at least one* grade or group is a spec
question outside the current FR-58 scope (see review doc §4 item 5).

## Related files

- `src/Assignments/SchoolCollab.Assignments.Api/Services/TopicAssignmentLookupHttpClient.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/PublishAssignmentCommand/PublishAssignmentCommandHandler.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Services/ITopicAssignmentLookup.cs`
- `src/Students/SchoolCollab.Students.Api/Endpoints/TopicAssignmentRoutes.cs`
