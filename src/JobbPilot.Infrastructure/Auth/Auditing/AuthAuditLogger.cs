using JobbPilot.Application.Common.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace JobbPilot.Infrastructure.Auth.Auditing;

public sealed partial class AuthAuditLogger(
    ILogger<AuthAuditLogger> logger,
    IHttpContextAccessor httpContextAccessor)
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

    public void LogoutSucceeded(Guid userId, string sessionIdPrefix)
    {
        var (resolvedIp, _) = ExtractRequestContext();
        LogLogoutSucceeded(logger, "logout_succeeded", userId, sessionIdPrefix, resolvedIp);
    }

    private (string ip, string userAgent) ExtractRequestContext()
    {
        var ctx = httpContextAccessor.HttpContext;
        var ip = ctx?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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

    [LoggerMessage(1003, LogLevel.Information,
        "AuditEvent={AuditEvent} UserId={UserId} SessionIdPrefix={SessionIdPrefix} Ip={Ip}")]
    private static partial void LogLogoutSucceeded(
        ILogger logger, string auditEvent, Guid userId, string sessionIdPrefix, string ip);
}
