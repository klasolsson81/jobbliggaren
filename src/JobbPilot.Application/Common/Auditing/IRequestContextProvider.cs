namespace JobbPilot.Application.Common.Auditing;

/// <summary>
/// Producerar IP-adress + User-Agent för audit-rad. Per ADR 0022.
/// Returvärden är nullable — Worker-jobb och tester kan returnera null
/// utan att audit-skrivningen kraschar.
/// </summary>
public interface IRequestContextProvider
{
    string? IpAddress { get; }
    string? UserAgent { get; }
}
