using Microsoft.Extensions.Logging;

namespace SchoolCollab.Admin.Shared.Services;

/// <summary>
/// <b>The one place a caught exception becomes text a user reads.</b> The helper logs the exception
/// with its full detail — type, message, stack, inner exceptions — and returns only the caller's plain
/// sentence, so a message bar can never show a stack frame, a socket error, a SQL error or an internal
/// type name to a teacher, a guardian or an administrator.
///
/// <para><b>Why a helper and not a convention.</b> The admin surface had <c>_error = ex.Message</c> in
/// well over a hundred <c>.razor</c> call sites, each of which looked correct in review: it sets an
/// error, it shows an error, and nothing fails. What it leaks is invisible until the day a request
/// times out and the operator reads "An error occurred while sending the request" beside a Save button,
/// or a foreign-key violation reaches a parent's screen. The rule is therefore enforced by
/// <c>FriendlyErrorArchitectureTests</c> in <c>SchoolCollab.ArchitectureTests.Unit</c>, which forbids a
/// <c>Message</c> read off a caught exception inside any <c>.razor</c> file; this type is the migration
/// target that scan points at.</para>
///
/// <para><b>Each call site keeps its own wording.</b> The sentence names what failed and what the reader
/// can do about it ("Teachers could not be loaded. Try again…"), because one generic "Something went
/// wrong" would trade a leak for a shrug. That is why the wording is a parameter rather than something
/// this helper invents.</para>
///
/// <para><b>Do not pass the exception's own message as the sentence</b> — that is the defect this
/// exists to prevent, and it would satisfy the guard while defeating it.</para>
/// </summary>
/// <example>
/// <code>
/// catch (Exception ex)
/// {
///     _error = FriendlyError.Describe(ex, "Teachers could not be loaded. Try again.", Logger);
/// }
/// </code>
/// </example>
public static class FriendlyError
{
    /// <summary>
    /// Logs <paramref name="exception"/> with full detail and returns <paramref name="plainSentence"/>
    /// — nothing from the exception — for display.
    /// </summary>
    /// <param name="exception">The caught exception. Logged; never rendered.</param>
    /// <param name="plainSentence">The site's own user-facing sentence: what failed, and what the
    /// reader can do next. Must read as a sentence a non-technical adult would understand.</param>
    /// <param name="logger">The component's logger, so the entry carries the component's category.</param>
    /// <returns><paramref name="plainSentence"/>, unchanged.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="plainSentence"/> is blank — a blank bar
    /// renders nothing, which would hide the failure entirely.</exception>
    public static string Describe(Exception exception, string plainSentence, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(plainSentence);

        // The sentence doubles as the log message: it names the operation in the operator's own words,
        // and the exception attached to the entry carries the type, message, stack and inner chain.
        logger.LogError(exception, "Surfaced to the user: {PlainSentence}", plainSentence);

        return plainSentence;
    }
}
