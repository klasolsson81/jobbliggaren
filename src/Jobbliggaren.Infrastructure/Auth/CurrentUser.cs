using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Jobbliggaren.Infrastructure.Auth;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal =>
        httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var sub = Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated == true;

    public string? Jti => Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);

    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email)
        ?? Principal?.FindFirstValue(ClaimTypes.Email);

    public SessionId? SessionId =>
        httpContextAccessor.HttpContext?.Items["SessionId"] is SessionId sid ? sid : null;

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
}
