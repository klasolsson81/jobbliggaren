using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.Refresh;

public sealed record RefreshCommand(string? RefreshToken) : ICommand<Result<AuthTokensDto>>;
