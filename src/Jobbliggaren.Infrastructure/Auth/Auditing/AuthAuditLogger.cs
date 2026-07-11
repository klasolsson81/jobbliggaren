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
    public void LoginSucceeded(Guid userId, string sessionIdPrefix)
    {
        var (resolvedIp, resolvedAgent) = ExtractRequestContext();
        LogLoginSucceeded(logger, "login_succeeded", userId, sessionIdPrefix, resolvedIp, resolvedAgent);
    }

    public void LoginFailed(string emailHash)
    {
        var (resolvedIp, resolvedAgent) = ExtractRequestContext();
        LogLoginFailed(logger, "login_failed", emailHash, resolvedIp, resolvedAgent);
    }

    public void AccountLockedOut(string emailHash)
    {
        var (resolvedIp, resolvedAgent) = ExtractRequestContext();
        LogAccountLockedOut(logger, "account_locked_out", emailHash, resolvedIp, resolvedAgent);
    }

    public void LogoutSucceeded(Guid userId, string sessionIdPrefix)
    {
        var (resolvedIp, _) = ExtractRequestContext();
        LogLogoutSucceeded(logger, "logout_succeeded", userId, sessionIdPrefix, resolvedIp);
    }

    public void EmailConfirmationResent(Guid userId)
    {
        var (resolvedIp, resolvedAgent) = ExtractRequestContext();
        LogEmailConfirmationResent(logger, "email_confirmation_resent", userId, resolvedIp, resolvedAgent);
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
        "AuditEvent={AuditEvent} UserId={UserId} SessionIdPrefix={SessionIdPrefix} Ip={Ip} UserAgent={UserAgent}")]
    private static partial void LogLoginSucceeded(
        ILogger logger, string auditEvent, Guid userId, string sessionIdPrefix, string ip, string userAgent);

    [LoggerMessage(1002, LogLevel.Warning,
        "AuditEvent={AuditEvent} EmailHash={EmailHash} Ip={Ip} UserAgent={UserAgent}")]
    private static partial void LogLoginFailed(
        ILogger logger, string auditEvent, string emailHash, string ip, string userAgent);

    [LoggerMessage(1004, LogLevel.Warning,
        "AuditEvent={AuditEvent} EmailHash={EmailHash} Ip={Ip} UserAgent={UserAgent}")]
    private static partial void LogAccountLockedOut(
        ILogger logger, string auditEvent, string emailHash, string ip, string userAgent);

    [LoggerMessage(1003, LogLevel.Information,
        "AuditEvent={AuditEvent} UserId={UserId} SessionIdPrefix={SessionIdPrefix} Ip={Ip}")]
    private static partial void LogLogoutSucceeded(
        ILogger logger, string auditEvent, Guid userId, string sessionIdPrefix, string ip);

    [LoggerMessage(1005, LogLevel.Information,
        "AuditEvent={AuditEvent} UserId={UserId} Ip={Ip} UserAgent={UserAgent}")]
    private static partial void LogEmailConfirmationResent(
        ILogger logger, string auditEvent, Guid userId, string ip, string userAgent);
}
