using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace JobbPilot.Infrastructure.Auth;

public sealed class JwtTokenGenerator(
    IOptions<JwtSettings> settings,
    IDateTimeProvider clock)
    : IJwtTokenGenerator
{
    private readonly JwtSettings _settings = settings.Value;

    public GeneratedTokens GenerateTokens(Guid userId, string email, IEnumerable<string> roles)
    {
        var now = clock.UtcNow;
        var accessExpires = now.AddMinutes(_settings.AccessTokenLifetimeMinutes);
        var refreshExpires = now.AddDays(_settings.RefreshTokenLifetimeDays);

        var jti = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var privateKey = LoadPrivateKey();
        var signingCredentials = new SigningCredentials(
            new RsaSecurityKey(privateKey),
            SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: accessExpires.UtcDateTime,
            signingCredentials: signingCredentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        return new GeneratedTokens(
            AccessToken: accessToken,
            RefreshToken: rawRefreshToken,
            AccessTokenExpiresAt: accessExpires,
            RefreshTokenExpiresAt: refreshExpires);
    }

    public string HashToken(string rawToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private RSA LoadPrivateKey()
    {
        var pem = File.ReadAllText(_settings.PrivateKeyPath);
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }
}
