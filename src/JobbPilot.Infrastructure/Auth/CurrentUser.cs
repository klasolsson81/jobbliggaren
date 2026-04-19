using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using JobbPilot.Application.Common.Abstractions;
using Microsoft.AspNetCore.Http;

namespace JobbPilot.Infrastructure.Auth;

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
}
