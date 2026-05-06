namespace JobbPilot.Application.Common.Configuration;

public sealed record JwtSettings
{
    public const string SectionName = "Jwt";

    public int AccessTokenLifetimeMinutes { get; init; } = 15;
}
