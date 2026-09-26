using Jobbliggaren.Application.Auth.ExternalLogins;
using Mediator;

namespace Jobbliggaren.Application.Auth.Queries.GetExternalLoginProviders;

public sealed class GetExternalLoginProvidersQueryHandler(RegisteredProviders providers)
    : IQueryHandler<GetExternalLoginProvidersQuery, IReadOnlyList<string>>
{
    public ValueTask<IReadOnlyList<string>> Handle(
        GetExternalLoginProvidersQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<string>>(providers.Keys.Select(key => key.Value).ToList());
}
