using JobbPilot.Infrastructure.Auth.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Auth;

/// <summary>
/// Verifierar att AuthAuditLogger emitterar korrekt event-shape per EventId.
/// Kritisk test: LoginFailed skickar EmailHash, INTE raw email.
/// </summary>
public class AuthAuditLoggerTests
{
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Records { get; } = [];

        public (LogLevel Level, EventId EventId, string Message) Latest => Records[^1];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Records.Add((logLevel, eventId, formatter(state, exception)));
    }

    private static (AuthAuditLogger logger, RecordingLogger<AuthAuditLogger> recorder)
        CreateLogger(string? ip = "1.2.3.4", string? userAgent = "TestAgent/1.0")
    {
        var recorder = new RecordingLogger<AuthAuditLogger>();
        var accessor = Substitute.For<IHttpContextAccessor>();
        var ctx = Substitute.For<HttpContext>();
        var conn = Substitute.For<ConnectionInfo>();
        var req = Substitute.For<HttpRequest>();
        var headers = new HeaderDictionary();
        if (userAgent is not null) headers["User-Agent"] = userAgent;

        conn.RemoteIpAddress.Returns(
            ip is not null ? System.Net.IPAddress.Parse(ip) : null);
        ctx.Connection.Returns(conn);
        ctx.Request.Returns(req);
        req.Headers.Returns(headers);
        accessor.HttpContext.Returns(ctx);

        return (new AuthAuditLogger(recorder, accessor), recorder);
    }

    [Fact]
    public void LoginSucceeded_EmitsEventId1001_Information()
    {
        var (sut, recorder) = CreateLogger();

        sut.LoginSucceeded(Guid.NewGuid(), "abc123…");

        recorder.Latest.EventId.Id.ShouldBe(1001);
        recorder.Latest.Level.ShouldBe(LogLevel.Information);
    }

    [Fact]
    public void LoginSucceeded_ContainsUserId()
    {
        var (sut, recorder) = CreateLogger();
        var userId = Guid.NewGuid();

        sut.LoginSucceeded(userId, "abc123…");

        recorder.Latest.Message.ShouldContain(userId.ToString());
    }

    [Fact]
    public void LoginFailed_EmitsEventId1002_Warning()
    {
        var (sut, recorder) = CreateLogger();

        sut.LoginFailed("deadbeef1234");

        recorder.Latest.EventId.Id.ShouldBe(1002);
        recorder.Latest.Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void LoginFailed_ContainsEmailHashNotRawEmail()
    {
        var (sut, recorder) = CreateLogger();
        const string rawEmail = "secret@example.com";
        const string emailHash = "a1b2c3d4";

        sut.LoginFailed(emailHash);

        recorder.Latest.Message.ShouldContain(emailHash);
        recorder.Latest.Message.ShouldNotContain(rawEmail);
    }

    [Fact]
    public void LogoutSucceeded_EmitsEventId1003_Information()
    {
        var (sut, recorder) = CreateLogger();

        sut.LogoutSucceeded(Guid.NewGuid(), "abc123…");

        recorder.Latest.EventId.Id.ShouldBe(1003);
        recorder.Latest.Level.ShouldBe(LogLevel.Information);
    }

    [Fact]
    public void LogoutSucceeded_ContainsUserId()
    {
        var (sut, recorder) = CreateLogger();
        var userId = Guid.NewGuid();

        sut.LogoutSucceeded(userId, "abc123…");

        recorder.Latest.Message.ShouldContain(userId.ToString());
    }

    [Fact]
    public void LoginSucceeded_ExtractsIpFromHttpContext()
    {
        var (sut, recorder) = CreateLogger(ip: "10.0.0.1");

        sut.LoginSucceeded(Guid.NewGuid(), "prefix…");

        recorder.Latest.Message.ShouldContain("10.0.0.1");
    }
}
