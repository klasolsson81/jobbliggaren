using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Helpers;

/// <summary>
/// Constructors for realistic degraded-Redis faults, used by the session-store resilience and
/// observability suites.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why only the server fault lives here.</b> The three sibling exception types are handled three
/// different ways, and that asymmetry is deliberate rather than drift — it mirrors a real difference
/// in what the library exposes. Do NOT "tidy" it by routing all three through this class or by
/// suppressing all three uniformly.
/// </para>
/// <list type="bullet">
///   <item><see cref="RedisTimeoutException"/> — constructed inline; its
///     <c>(CommandFlags, string, CommandStatus)</c> overload is clean.</item>
///   <item><see cref="RedisConnectionException"/> — constructed inline; its
///     <c>(ConnectionFailureType, CommandFlags, string, Exception, CommandStatus)</c> overload is
///     clean.</item>
///   <item><see cref="RedisServerException"/> — <b>has no clean overload</b>, hence this factory.</item>
/// </list>
/// <para>
/// <b>The suppression, and why it is not hiding anything.</b> Measured against StackExchange.Redis
/// 3.1.0: <see cref="RedisServerException"/> has exactly two public constructors. <c>(string)</c> is
/// <c>[Obsolete]</c> → CS0618. The prescribed successor,
/// <c>(RedisErrorKind, CommandFlags, string)</c>, is <c>[Experimental]</c> → <b>SER007</b>:
/// <i>"for evaluation purposes only and is subject to change or removal in future updates"</i>.
/// A deprecation whose only prescribed replacement is marked removable is a signal without a
/// destination, so suppressing it names a fact rather than concealing one.
/// </para>
/// <para>
/// <b>Why the obsolete constructor is the safer of the two suppressions.</b> Taking the experimental
/// API would suppress *instability* and then depend on it: if <c>RedisErrorKind</c> is withdrawn the
/// constructor goes with it and we land back on <c>(string)</c> having paid the migration twice.
/// The obsolete constructor, by contrast, is structurally protected — <see cref="RedisServerException"/>
/// is <c>sealed</c> (verified with the compiler: deriving from it gives CS0509) with exactly those two
/// constructors, so the library cannot remove <c>(string)</c> while its only successor is still marked
/// removable without leaving a public sealed exception type unconstructible outside an experimental API.
/// </para>
/// <para>
/// <b>Lift condition.</b> When <c>RedisErrorKind</c> ships without <c>[Experimental]</c>, replace this
/// body with <c>new RedisServerException(RedisErrorKind.Loading, CommandFlags.None, message)</c> and
/// delete the pragma. One place, one line.
/// </para>
/// <para>
/// Nothing here changes what the suites prove: production classifies faults by TYPE only
/// (<c>ex is RedisException or RedisTimeoutException</c>), never by error kind.
/// </para>
/// </remarks>
internal static class RedisFaults
{
    /// <summary>
    /// A server-side fault of the LOADING family — what a real Redis emits while restoring an RDB
    /// snapshot. Derives from <see cref="RedisException"/>, which is the arm of the production filter
    /// these suites exercise (as distinct from <see cref="RedisTimeoutException"/>, which does not).
    /// </summary>
    /// <param name="message">
    /// Passed through verbatim. Callers rely on the exact text: <c>SessionStoreUnavailableTests</c>
    /// pins that a fault message never reaches the log, and a paraphrase would void that assertion.
    /// </param>
    internal static RedisServerException Loading(string message) =>
#pragma warning disable CS0618 // Successor ctor is [Experimental]/SER007 — see the remarks above.
        new(message);
#pragma warning restore CS0618
}
