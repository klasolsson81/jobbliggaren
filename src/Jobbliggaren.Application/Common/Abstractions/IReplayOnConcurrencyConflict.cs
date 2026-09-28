namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Marks a command that <c>UnitOfWorkBehavior</c> re-runs on a fresh read when its commit loses an
/// optimistic-concurrency race (ADR 0146). On a conflict the behaviour clears the change tracker and
/// runs the rest of the pipeline again, up to <c>UnitOfWorkBehavior.MaxAttempts</c> attempts in all;
/// the last conflict throws <see cref="Exceptions.ConcurrencyConflictException"/>, which the Api
/// maps to 409.
/// <para>
/// A marked command's handler re-derives everything from what it reads, and it has no side effect
/// before the commit (no email, no job, no Redis write): an attempt that conflicts may leave nothing
/// behind but a rolled-back transaction.
/// </para>
/// </summary>
public interface IReplayOnConcurrencyConflict;
