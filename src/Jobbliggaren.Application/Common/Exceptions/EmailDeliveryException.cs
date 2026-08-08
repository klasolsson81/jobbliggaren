namespace Jobbliggaren.Application.Common.Exceptions;

/// <summary>
/// Thrown by an <see cref="Abstractions.IEmailSender"/> implementation when a send fails. Carries
/// the email KIND and the underlying exception's TYPE NAME, and deliberately nothing else
/// (ADR 0124; senior-cto-advisor bind 4, 2026-08-08, on a security-auditor Major).
///
/// <para>
/// <b>Why the provider's own exception must not escape the adapter.</b> Amazon SES embeds the
/// recipient address in its error messages — in the sandbox every recipient must be verified, and
/// the failure names the failing identity. Many <c>[LoggerMessage]</c> declarations across
/// <c>src/</c> forward an <see cref="Exception"/> object to the sink (the count and its grep live
/// in ADR 0124), and the sink is durable whenever <c>Seq:ServerUrl</c> is set. Patching those call
/// sites was rejected as an enumeration that cannot be completed: <c>Api/Program.cs</c> has NO
/// generic <c>catch (Exception)</c>, no <c>UseExceptionHandler</c> and no
/// <c>UseDeveloperExceptionPage</c>, so an unmatched provider exception escapes the application's
/// own handler chain and is logged by framework code no diff can reach.
/// </para>
///
/// <para>
/// <b><see cref="Exception.InnerException"/> is deliberately EMPTY, and this is the detail that
/// will otherwise be "fixed" back.</b> .NET's exception formatting walks the whole inner chain
/// INCLUDING messages, so attaching the provider exception would carry the recipient address to the
/// sink through this wrapper and defeat the entire point. The obvious counter-precedent does not
/// transfer: <c>SessionStoreUnavailableException</c> DOES keep its inner exception, and
/// <c>Api/Program.cs</c> states why in its own comment — <i>"Auth runs outside the Mediator
/// pipeline, so LoggingBehavior never sees this."</i> Email runs INSIDE that pipeline, and
/// <c>LoggingBehavior</c> logs the exception object. Same shape, opposite conclusion.
/// </para>
///
/// <para>
/// Diagnostics are not lost, only relocated: the sender logs kind + underlying type at Error
/// before throwing, and the provider's own console retains the full detail against a message id
/// this codebase deliberately does not capture.
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
