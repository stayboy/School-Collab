namespace SchoolCollab.Settings.Api.Endpoints;

// Response payload shapes for the CodedValue routes. They exist only so a handler can name the arm of a
// TypedResults union: an anonymous type cannot appear in Results<Created<…>, Conflict<…>>. Each record
// reproduces its anonymous predecessor property-for-property, so the serialized JSON is unchanged.
// Spec: minimal-api-typed-responses (D11 + round correction, 2026-10-17).

internal sealed record IdResponse(Guid Id);

internal sealed record MessageResponse(string Message);

internal sealed record BulkCreateResponse(Guid ParentId, int CreatedCount, IReadOnlyList<string> SkippedCodes);
