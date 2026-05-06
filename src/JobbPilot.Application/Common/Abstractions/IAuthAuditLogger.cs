namespace JobbPilot.Application.Common.Abstractions;

public interface IAuthAuditLogger
{
    void LoginSucceeded(Guid userId, string sessionIdPrefix);
    void LoginFailed(string emailHash);
    void LogoutSucceeded(Guid userId, string sessionIdPrefix);
}
