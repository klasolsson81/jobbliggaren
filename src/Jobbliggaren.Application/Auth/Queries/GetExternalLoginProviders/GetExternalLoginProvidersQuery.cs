using Jobbliggaren.Application.Auth.ExternalLogins;
using Mediator;

namespace Jobbliggaren.Application.Auth.Queries.GetExternalLoginProviders;

/// <summary>
/// #1744 — the providers the login page may offer (ADR 0142 D8): the keys this host registered, in the order the
/// page lists them. Empty on a host without keys, which is the whole switch: there is no flag.
/// </summary>
public sealed record GetExternalLoginProvidersQuery : IQuery<IReadOnlyList<string>>;

public sealed class GetExternalLoginProvidersQueryHandler(RegisteredProviders providers)
    : IQueryHandler<GetExternalLoginProvidersQuery, IReadOnlyList<string>>
{
    public ValueTask<IReadOnlyList<string>> Handle(
        GetExternalLoginProvidersQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<string>>(providers.Keys.Select(key => key.Value).ToList());
}
