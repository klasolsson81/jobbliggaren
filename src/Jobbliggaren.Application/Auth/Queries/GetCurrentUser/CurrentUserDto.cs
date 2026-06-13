namespace Jobbliggaren.Application.Auth.Queries.GetCurrentUser;

public sealed record CurrentUserDto(
    Guid UserId,
    string Email,
    IReadOnlyList<string> Roles);
