namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// A named per-subject counter: at most <see cref="Limit"/> admitted calls per fixed
/// <see cref="Window"/>, the window starting at the first counted call. The scope carries its own limit
/// and window, so no caller can pair one scope with two windows. <see cref="Name"/> becomes part of the
/// Redis key, so a value MUST NOT change once shipped (in-flight counters would reset) and distinct
/// budgets MUST NOT share one (their counts would collide).
/// </summary>
public sealed record RateBudgetScope
{
    public RateBudgetScope(string name, int limit, TimeSpan window)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        Name = name;
        Limit = limit;
        Window = window;
    }

    public string Name { get; }

    public int Limit { get; }

    public TimeSpan Window { get; }
}

/// <summary>
/// Counts calls against a <see cref="RateBudgetScope"/> for one subject (ADR 0142 D1/D2). Policy-free: it
/// answers "admitted or not", and the caller decides what a refusal means. A refused call is still counted,
/// so a caller that must not spend the budget on work it will not do checks its cheaper gates FIRST.
/// </summary>
public interface IRateBudget
{
    /// <summary>
    /// Counts one call for <paramref name="subject"/> (the implementation normalises and hashes it, never
    /// writing the raw value) and returns whether the count is within
    /// <see cref="RateBudgetScope.Limit"/> for the current window.
    /// </summary>
    Task<bool> TryConsumeAsync(RateBudgetScope scope, string subject, CancellationToken ct);
}
