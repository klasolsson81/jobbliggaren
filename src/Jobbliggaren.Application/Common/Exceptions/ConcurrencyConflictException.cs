namespace Jobbliggaren.Application.Common.Exceptions;

/// <summary>
/// Thrown when a replayable command exhausts its attempts or a protected account mutation loses
/// a precommit concurrency race after rollback. Nothing was committed. Mapped centrally in the Api to a 409
/// with a fixed title; the message is fixed and carries no entity value.
/// </summary>
public sealed class ConcurrencyConflictException()
    : Exception("Uppgifterna ändrades av en samtidig skrivning.");
