using System.Linq.Expressions;

namespace Jobbliggaren.Domain.Common;

/// <summary>
/// One predicate in two forms (AGENTS.md §3.6): <see cref="Criteria"/> for a query, which EF translates
/// to SQL, and <see cref="IsSatisfiedBy"/> for an instance already in memory. Both come from the one
/// expression.
/// </summary>
public sealed class Specification<T>(Expression<Func<T, bool>> criteria)
{
    private readonly Func<T, bool> _compiled = criteria.Compile();

    /// <summary>
    /// The query form. Pass this to <c>Where</c>, never <see cref="IsSatisfiedBy"/>: a method group
    /// runs client-side and loads the whole table first.
    /// </summary>
    public Expression<Func<T, bool>> Criteria { get; } = criteria;

    public bool IsSatisfiedBy(T candidate) => _compiled(candidate);
}
