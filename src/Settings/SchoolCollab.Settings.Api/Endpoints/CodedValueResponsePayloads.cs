namespace SchoolCollab.Settings.Api.Endpoints;

// Response payload shapes for the CodedValue routes. They exist only so a handler can name the arm of a
// TypedResults union: an anonymous type cannot appear in Results<Created<…>, Conflict<…>>. Each record
// reproduces its anonymous predecessor property-for-property, so the serialized JSON is unchanged.
// Spec: minimal-api-typed-responses (D11 + round correction, 2026-10-17). The cross-aggregate
// MessageResponse moved to ApiResponsePayloads.cs (spec minimal-api-typed-responses-settings D17),
// and IdResponse is the shared single-id envelope: the config-flag and entity-code creates reuse it.

internal sealed record IdResponse(Guid Id);

internal sealed record BulkCreateResponse(Guid ParentId, int CreatedCount, IReadOnlyList<string> SkippedCodes);
