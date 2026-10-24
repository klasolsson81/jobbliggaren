using Jobbliggaren.Application.Admin.Email.Dmarc;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0184) — the adapter's own lines: an event id, a closed reason and the step it happened in. Never an
/// address, a sender, a subject, a file name, a UID, a server's text or an exception's message, since anyone can mail
/// the report address and the server's text can quote what they wrote.
/// </summary>
internal static partial class DmarcMailboxLog
{
    [LoggerMessage(3135, LogLevel.Warning, "[DmarcMailbox] The mailbox could not be opened: {Fault} during {Step}")]
    public static partial void OpenFailed(this ILogger logger, DmarcSessionFault fault, DmarcMailboxStep step);

    [LoggerMessage(3136, LogLevel.Warning,
        "[DmarcMailbox] The server does not advertise the sign-in mechanism; no credential was sent")]
    public static partial void MechanismNotAdvertised(this ILogger logger);

    [LoggerMessage(3137, LogLevel.Warning, "[DmarcMailbox] The session ended: {Fault} during {Step}")]
    public static partial void SessionFault(this ILogger logger, DmarcSessionFault fault, DmarcMailboxStep step);

    [LoggerMessage(3138, LogLevel.Information, "[DmarcMailbox] The server refused one message's command during {Step}")]
    public static partial void Refused(this ILogger logger, DmarcMailboxStep step);

    [LoggerMessage(3139, LogLevel.Information, "[DmarcMailbox] The logout did not complete ({ExceptionType})")]
    public static partial void LogoutIncomplete(this ILogger logger, string exceptionType);
}

/// <summary>Where in a session something happened. Logged by name.</summary>
internal enum DmarcMailboxStep
{
    Connect,
    Authenticate,
    Examine,
    List,
    FetchHeader,
    ProbePart,
    FetchPart,
}
