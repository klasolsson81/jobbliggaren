namespace Jobbliggaren.Application.Common.Exceptions;

/// <summary>
/// Thrown by an <see cref="Abstractions.IEmailSender"/> implementation when a send fails. Carries
/// the email KIND and the underlying exception's TYPE NAME, and deliberately nothing else
/// (ADR 0124; senior-cto-advisor bind 4, 2026-08-08, on a security-auditor Major).
///
/// <para>
/// <b>Why the provider's own exception must not escape the adapter.</b> A rejection names the
/// recipient it is about. Under SES that address sat in the exception MESSAGE; under Scaleway
/// (#183) it sits in the error RESPONSE BODY, which is why <c>ScalewayEmailSender</c> never reads
/// that body at all. The rule outlived the provider it was written against, and its evidence is now
/// a test rather than a claim: the sender suite puts the address in a 4xx body and measures that it
/// reaches neither the log nor this exception. Many <c>[LoggerMessage]</c> declarations across
/// <c>src/</c> forward an <see cref="Exception"/> object to the sink (the count and its grep live
/// in ADR 0124), and the sink is durable whenever <c>Seq:ServerUrl</c> is set. Patching those call
/// sites was rejected as an enumeration that cannot be completed: <c>Api/Program.cs</c> has NO
/// generic <c>catch (Exception)</c>, no <c>UseExceptionHandler</c> and no
/// <c>UseDeveloperExceptionPage</c>, so an unmatched provider exception escapes the application's
/// own handler chain and is logged by framework code no diff can reach.
/// </para>
///
/// <para>
/// <b><see cref="Exception.InnerException"/> is deliberately EMPTY, and it is the detail that will
/// otherwise be "fixed" back.</b> Exception formatting walks the inner chain including messages,
/// so attaching the provider exception would carry the address through this wrapper.
/// <c>SessionStoreUnavailableException</c> keeps its inner exception for a reason that does not
/// transfer, stated in <c>Api/Program.cs</c>: auth runs OUTSIDE the Mediator pipeline. Email runs
/// inside it, and <c>LoggingBehavior</c> logs the exception object.
/// </para>
/// </summary>
public sealed class EmailDeliveryException(string emailKind, string underlyingErrorType)
    : Exception($"Email delivery failed for '{emailKind}' ({underlyingErrorType}).")
{
    /// <summary>The kebab-case email kind, e.g. <c>email-confirmation</c>. Never PII.</summary>
    public string EmailKind { get; } = emailKind;

    /// <summary>
    /// The underlying exception's <see cref="Type.Name"/> — the type ONLY, never its message.
    /// </summary>
    public string UnderlyingErrorType { get; } = underlyingErrorType;
}
