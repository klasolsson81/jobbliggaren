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

    // #822: Email är borttaget. SessionAuthenticationHandler emit:ar ingen e-post-claim
    // (bara NameIdentifier/Sub/session_id_prefix) — den enda emitteringen fanns i den
    // avvecklade JwtTokenGenerator — så egenskapen returnerade alltid null under den
    // auth-scheman som faktiskt kör. E-post hämtas ur identity-storen via
    // IUserAccountService.GetEmailAsync.

    public SessionId? SessionId =>
        httpContextAccessor.HttpContext?.Items["SessionId"] is SessionId sid ? sid : null;

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
}
