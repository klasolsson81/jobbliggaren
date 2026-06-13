namespace Jobbliggaren.Domain.Common;

/// <summary>
/// Kastas när ett aggregate-invariant bryts i runtime (t.ex. EF Core-rehydrering
/// levererar inkonsistent state). Distinkt från <see cref="DomainError"/> som
/// representerar expected/recoverable validation-fel via <see cref="Result"/>.
/// Fångas i Api-middleware och returnerar 400 enligt CLAUDE.md §3.4.
/// </summary>
/// <remarks>
/// <para>
/// Boendet i Domain-lagret är medvetet: invariant-skydd är aggregate-rotens
/// ansvar och uttrycks i Domain (Evans 2003, "Aggregates"). Exception-typen
/// måste därför också finnas där — annars skulle Domain bero på Application,
/// vilket bryter dependency-regeln (CLAUDE.md §2.1).
/// </para>
/// <para>
/// Kontrast mot <c>Jobbliggaren.Application.Common.Exceptions</c>
/// (<c>NotFoundException</c>, <c>ValidationException</c>, <c>ForbiddenException</c>,
/// <c>UnauthorizedException</c>): de är handler-flow-signaler som mappar mot
/// HTTP-protokoll. <c>DomainException</c> är specifikt för invariant-brott —
/// situationer som inte ska kunna hända via legitima command-paths.
/// </para>
/// </remarks>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
