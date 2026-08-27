using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Api.Observability;

/// <summary>
/// Source-generated (CA1848) startup announcement for the throwaway dev-tooling flags — today
/// only <c>DevTools:EnableResetMyData</c>. REMOVE BEFORE LAUNCH with the flags themselves
/// (<c>docs/runbooks/release-checklist.md</c>).
/// <para>
/// There is deliberately no Information-level counterpart. The registration gate announces both
/// polarities because CLOSED is a posture someone may need to confirm; here the OFF state is the
/// default and the absence of the route is already observable as a 404. Only the live-in-a-
/// deployed-environment case is worth a line, and it is worth an alertable one.
/// </para>
/// </summary>
internal static partial class DevToolsLog
{
    // No parameter: this line fires on exactly one condition, so a {DevToolState} property
    // could only ever read "ENABLED" and would carry nothing the EventId does not. The
    // registration gate parameterises because a Seq query there must match TWO lines.
    [LoggerMessage(EventId = 4310, Level = LogLevel.Warning,
        Message = "Dev tool reset-my-data: ENABLED outside Development")]
    public static partial void AnnounceResetMyDataEnabledOutsideDevelopment(ILogger logger);
}
