namespace Jobbliggaren.Application.Common.Exceptions;

/// <summary>
/// Thrown by <c>UnitOfWorkBehavior</c> when an
/// <see cref="Abstractions.IReplayOnConcurrencyConflict"/> command lost every attempt to a concurrent
/// write (ADR 0146). Nothing was committed. Mapped centrally in the Api (<c>Program.cs</c>) to a 409
/// with a fixed title; the message is fixed and carries no entity value.
/// </summary>
public sealed class ConcurrencyConflictException()
    : Exception("Raden ändrades av en samtidig skrivning vid varje försök.");
