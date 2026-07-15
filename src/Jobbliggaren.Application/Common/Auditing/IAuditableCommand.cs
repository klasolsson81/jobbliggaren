using Jobbliggaren.Application.Common.Security;

namespace Jobbliggaren.Application.Common.Auditing;

/// <summary>
/// Marker-interface för commands som ska generera audit-rad. Per ADR 0022
/// triggar AuditBehavior endast på commands som implementerar
/// <see cref="IAuditableCommand{TResponse}"/>. Värden från detta interface
/// skrivs till audit_log-tabellen (BUILD.md §7.1).
/// </summary>
public interface IAuditableCommand
{
    /// <summary>
    /// Stabilt event-namn skrivet till audit_log.event_type. Format:
    /// "<AggregateType>.<Action>" (t.ex. "Application.Created",
    /// "Resume.MasterContentUpdated"). Får inte ändras retroaktivt — audit
    /// queries beror på stabila värden.
    /// </summary>
    string EventType { get; }

    /// <summary>
    /// Aggregate-typ som muteras. Skrivs till audit_log.aggregate_type
    /// (t.ex. "Application", "Resume").
    /// </summary>
    string AggregateType { get; }

    /// <summary>
    /// Opt-in: also write an audit row when the handler returns <c>Result.Failure</c>. Default
    /// <c>false</c> — ADR 0022 Fas 1 audits success only, and that stays the rule for every
    /// existing command (OCP: a default interface implementation, so no existing command changes).
    /// </summary>
    /// <remarks>
    /// <b>Why any command needs this.</b> A rejected GDPR rights request must leave a trace that
    /// it was received and refused — Art. 12(3) requires us to tell the data subject the reasons
    /// for not acting and her right to complain, and we cannot do that from a record we never
    /// wrote. Today a rejected request vanishes silently. Blast radius of the opt-in: exactly the
    /// commands that set it.
    /// <para>
    /// <b>Contract for implementers who set this true:</b>
    /// <see cref="IAuditableCommand{TResponse}.ExtractAggregateId"/> is then also called on a
    /// FAILED response, so it must not read <c>Result.Value</c>. Return an id the command itself
    /// carries (e.g. a request id), not one the handler produced.
    /// </para>
    /// </remarks>
    bool AuditFailures => false;
}

/// <summary>
/// Opt-in: contribute a JSON payload to <c>audit_log.payload</c> (jsonb — the column has existed
/// since ADR 0022 and has never been written by a command).
/// </summary>
/// <remarks>
/// The <see cref="IIdentifierPseudonymizer"/> is handed in rather than injected into the command,
/// so the command stays a pure message while still being the thing that decides <i>what</i> is
/// recorded — and so a payload can never accidentally carry a raw identifier: the only route to
/// the record goes through the pseudonymiser.
/// </remarks>
public interface IAuditPayloadCommand<in TResponse>
{
    /// <summary>
    /// Returns the serialized JSON payload for this command's audit row, or <c>null</c> for none.
    /// Called on success, and on failure when <see cref="IAuditableCommand.AuditFailures"/> is set
    /// — so it must tolerate a failed <paramref name="response"/>.
    /// <b>Must never emit un-pseudonymised personal data</b> (CLAUDE.md §5).
    /// </summary>
    string? BuildAuditPayload(TResponse response, IIdentifierPseudonymizer pseudonymizer);
}

/// <summary>
/// Generic-marker som låter AuditBehavior extrahera aggregate-ID från
/// command response (för Create-fall där ID genereras i handler) eller
/// från command-fältet (för mutation av befintliga aggregat).
/// </summary>
public interface IAuditableCommand<TResponse> : IAuditableCommand
{
    /// <summary>
    /// Returnerar aggregate-ID för audit-raden. Anropas av AuditBehavior
    /// efter att handler kört och returnerat success.
    /// </summary>
    Guid ExtractAggregateId(TResponse response);
}
