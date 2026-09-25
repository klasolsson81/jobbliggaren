using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Auth.Auditing;

public sealed partial class AuthAuditLogger(
    ILogger<AuthAuditLogger> logger,
    IHttpContextAccessor httpContextAccessor,
    IIpAnonymizer ipAnonymizer)
    : IAuthAuditLogger
{
    public void LoginSucceeded(Guid userId, string sessionIdPrefix, LoginMethod method)
    {
        var (resolvedIp, resolvedAgent) = ExtractRequestContext();
        LogLoginSucceeded(logger, "login_succeeded", userId, sessionIdPrefix, method, resolvedIp, resolvedAgent);
    }

    public void LogoutSucceeded(Guid userId, string sessionIdPrefix)
    {
        var (resolvedIp, _) = ExtractRequestContext();
        LogLogoutSucceeded(logger, "logout_succeeded", userId, sessionIdPrefix, resolvedIp);
    }

    public void LoginChallengeIssued(
        Guid userId, LoginChallengeKind challengeKind, string? ipAddress, string? userAgent)
    {
        // Carried, not extracted. ExtractRequestContext() would return the "unknown" label here because the
        // caller is the dispatch consumer, with no HttpContext. The fallbacks match what ExtractRequestContext
        // produces, so a line's format does not tell which path wrote it.
        LogLoginChallengeIssued(
            logger,
            "login_challenge_issued",
            userId,
            challengeKind,
            ipAddress ?? IIpAnonymizer.UnknownLabel,
            userAgent ?? string.Empty);
    }

    public void ReauthenticationSucceeded(Guid userId, GrantPurpose purpose)
    {
        var (resolvedIp, resolvedAgent) = ExtractRequestContext();
        LogReauthenticationSucceeded(logger, "reauthentication_succeeded", userId, purpose, resolvedIp, resolvedAgent);
    }

    public void ReauthenticationFailed(Guid userId, GrantPurpose purpose)
    {
        var (resolvedIp, resolvedAgent) = ExtractRequestContext();
        LogReauthenticationFailed(logger, "reauthentication_failed", userId, purpose, resolvedIp, resolvedAgent);
    }

    // App-loggens IP/UA går genom samma anonymiserings-port som audit-tabellen
    // (ADR 0024 D7). Defense-in-depth: även om CloudWatch-retention (30d) failar
    // ska app-loggen inte bära unika IP-fingerprints.
    //
    // UA-trunkering duplicerar avsiktligt RequestContextProvider:s motsvarighet —
    // medveten skuld dokumenterad i tech-debt: UA är inte PII på samma nivå som
    // IP och 256-tröskeln matchar audit_log.user_agent-kolumnens längdgräns.
    private (string ip, string userAgent) ExtractRequestContext()
    {
        var ctx = httpContextAccessor.HttpContext;
        var rawIp = ctx?.Connection.RemoteIpAddress;
        var ip = rawIp is null ? IIpAnonymizer.UnknownLabel : ipAnonymizer.Anonymize(rawIp);
        var rawAgent = ctx?.Request.Headers.UserAgent.ToString() ?? string.Empty;
        var userAgent = rawAgent.Length > 256 ? rawAgent[..256] : rawAgent;
        return (ip, userAgent);
    }

    [LoggerMessage(1001, LogLevel.Information,
        "AuditEvent={AuditEvent} UserId={UserId} SessionIdPrefix={SessionIdPrefix} Method={Method} Ip={Ip} UserAgent={UserAgent}")]
    private static partial void LogLoginSucceeded(
        ILogger logger, string auditEvent, Guid userId, string sessionIdPrefix, LoginMethod method, string ip,
        string userAgent);

    [LoggerMessage(1003, LogLevel.Information,
        "AuditEvent={AuditEvent} UserId={UserId} SessionIdPrefix={SessionIdPrefix} Ip={Ip}")]
    private static partial void LogLogoutSucceeded(
        ILogger logger, string auditEvent, Guid userId, string sessionIdPrefix, string ip);

    // #1735. UserId and the mail kind only — never the address, the code or the link.
    [LoggerMessage(1011, LogLevel.Information,
        "AuditEvent={AuditEvent} UserId={UserId} ChallengeKind={ChallengeKind} Ip={Ip} UserAgent={UserAgent}")]
    private static partial void LogLoginChallengeIssued(
        ILogger logger, string auditEvent, Guid userId, LoginChallengeKind challengeKind, string ip, string userAgent);

    // #1739. Never the address, the code or the grant.
    [LoggerMessage(1019, LogLevel.Information,
        "AuditEvent={AuditEvent} UserId={UserId} Purpose={Purpose} Ip={Ip} UserAgent={UserAgent}")]
    private static partial void LogReauthenticationSucceeded(
        ILogger logger, string auditEvent, Guid userId, GrantPurpose purpose, string ip, string userAgent);

    [LoggerMessage(1020, LogLevel.Warning,
        "AuditEvent={AuditEvent} UserId={UserId} Purpose={Purpose} Ip={Ip} UserAgent={UserAgent}")]
    private static partial void LogReauthenticationFailed(
        ILogger logger, string auditEvent, Guid userId, GrantPurpose purpose, string ip, string userAgent);
}
