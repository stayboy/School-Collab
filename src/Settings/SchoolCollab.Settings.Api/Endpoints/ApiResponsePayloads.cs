namespace SchoolCollab.Settings.Api.Endpoints;

// Cross-aggregate 4xx response envelopes, shared by every route file in this host. They exist so a
// handler can name the arm of a TypedResults union: an anonymous type cannot appear in
// Results<Created<…>, Conflict<…>>. Each record reproduces its anonymous predecessor
// property-for-property, so the serialized JSON is unchanged.
// Spec: minimal-api-typed-responses-settings (D17).

/// <summary>4xx envelope serialized as <c>{"message":…}</c> — the <c>BadRequest</c>/<c>Conflict</c> payload.</summary>
internal sealed record MessageResponse(string Message);

/// <summary>
/// 400 envelope serialized as <c>{"error":…}</c>. Deliberately distinct from
/// <see cref="MessageResponse"/>: the two <c>TypedResults.Json(…, statusCode: 400)</c> sites already
/// put the message under an <c>error</c> key, so naming them must not normalise the key to
/// <c>message</c>.
/// </summary>
internal sealed record ErrorResponse(string Error);
