using System.Net;
using System.Text;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Security.BreachCheck;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Security;

/// <summary>
/// #616 — unit tests for the HIBP k-anonymity client against a FAKE <see cref="HttpMessageHandler"/>
/// (no network). Pins the CTO-bound protocol invariants: exactly the 5-char uppercase SHA-1 prefix
/// in the request URI (never the full hash), local case-insensitive suffix matching, the
/// count ≥ 1 rejection threshold with count-0 padding lines discarded, and every transport-failure
/// class mapping to <see cref="BreachCheckVerdict.Unavailable"/> with its bound fail-open reason —
/// while a genuine caller cancellation still throws. The Add-Padding header and resilience budget
/// are DI concerns pinned in <c>HibpBreachCheckResilienceTests</c> (real AddBreachedPasswordCheck).
/// </summary>
public class HibpPasswordBreachClientTests
{
    // SHA-1("password") — a publicly known digest of a publicly known breached password,
    // not a secret.
    private const string PasswordSha1 = "5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8"; // gitleaks:allow
    private const string Prefix = "5BAA6";
    private const string Suffix = "1E4C9B93F3F0682250B6CF8331B7EE68FD8";

    [Fact]
    public async Task CheckAsync_SendsExactlyTheUppercaseFiveCharPrefix_NeverTheFullHash()
    {
        var handler = new CannedHandler(_ => Ok($"{Suffix}:42"));
        var (client, _) = BuildClient(handler);

        await client.CheckAsync("password", TestContext.Current.CancellationToken);

        var uri = handler.LastRequestUri.ShouldNotBeNull();
        uri.AbsolutePath.ShouldBe($"/range/{Prefix}");
        uri.ToString().ShouldNotContain(Suffix, Case.Insensitive);
        uri.ToString().ShouldNotContain(PasswordSha1, Case.Insensitive);
    }

    [Fact]
    public async Task CheckAsync_MatchingSuffixWithCountOne_ReturnsBreached()
    {
        // Boundary: ANY occurrence (count >= 1) rejects — no higher magic threshold.
        var (client, _) = BuildClient(new CannedHandler(_ => Ok($"{Suffix}:1")));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.Breached);
    }

    [Fact]
    public async Task CheckAsync_MatchingSuffixAmongOtherLines_ReturnsBreached()
    {
        var body = $"""
            0018A45C4D1DEF81644B54AB7F969B88D65:3
            {Suffix}:10434004
            00D4F6E8FA6EECAD2A3AA415EEC418D38EC:2
            """;
        var (client, _) = BuildClient(new CannedHandler(_ => Ok(body)));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.Breached);
    }

    [Fact]
    public async Task CheckAsync_LowercaseResponseSuffix_ReturnsBreached()
    {
        // The wire contract is uppercase, but the local comparison is case-insensitive by design.
        var (client, _) = BuildClient(new CannedHandler(_ => Ok($"{Suffix.ToLowerInvariant()}:7")));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.Breached);
    }

    [Fact]
    public async Task CheckAsync_MatchingSuffixWithCountZero_ReturnsNotBreached()
    {
        // Add-Padding lines carry count 0 and must be discarded EVEN when the suffix matches.
        var (client, _) = BuildClient(new CannedHandler(_ => Ok($"{Suffix}:0")));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.NotBreached);
    }

    [Fact]
    public async Task CheckAsync_NoMatchingSuffix_ReturnsNotBreached()
    {
        var (client, _) = BuildClient(new CannedHandler(_ =>
            Ok("0018A45C4D1DEF81644B54AB7F969B88D65:3\n00D4F6E8FA6EECAD2A3AA415EEC418D38EC:2")));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.NotBreached);
    }

    [Fact]
    public async Task CheckAsync_MatchingSuffixWithNonNumericCount_ReturnsNotBreached()
    {
        // A non-numeric count on an otherwise-matching line must NOT throw. There is no catch for
        // a parse/format exception in the client, so a throw here would bubble through ValidateAsync
        // → Identity → an unhandled 500 on register/change-password, silently defeating fail-open.
        // long.TryParse fails closed to NotBreached instead (distinct branch from the count-0 case).
        var (client, _) = BuildClient(new CannedHandler(_ => Ok($"{Suffix}:notanumber")));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.NotBreached);
    }

    [Fact]
    public async Task CheckAsync_MalformedLinesBeforeMatch_AreSkipped_AndMatchStillWins()
    {
        // Defensive parsing of an untrusted upstream body (the code documents "malformed lines are
        // skipped"): a blank line (length branch), a too-short line (length branch), and a long
        // line without ':' at index 35 (colon branch) are all skipped without throwing, and a valid
        // match later in the body is still found.
        var body = string.Join('\n',
            string.Empty,           // blank line
            "short:1",              // shorter than a valid 35-hex-suffix line
            new string('X', 50),    // long enough to reach the ':' guard, but has no ':' at index 35
            $"{Suffix}:9");
        var (client, _) = BuildClient(new CannedHandler(_ => Ok(body)));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.Breached);
    }

    [Fact]
    public async Task CheckAsync_EmptyResponseBody_ReturnsNotBreached()
    {
        // A body with no lines (defensive edge — Add-Padding normally guarantees 800–1000 lines)
        // must resolve to NotBreached, never an enumeration over a null/empty reader that throws.
        var (client, _) = BuildClient(new CannedHandler(_ => Ok(string.Empty)));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.NotBreached);
    }

    [Fact]
    public async Task CheckAsync_UnicodePassword_HashesUtf8Bytes_NotUtf16OrAscii()
    {
        // Swedish product: a-ring / a-diaeresis / o-diaeresis passwords are realistic. The SHA-1
        // MUST be taken over the password's UTF-8 bytes. A silent switch to UTF-16 or ASCII would
        // change the request prefix and quietly stop matching exactly the non-ASCII passwords most
        // likely here, and fail-open would MASK the regression (a breached password would sail
        // through). The oracle below is precomputed over UTF-8; production's request prefix + suffix
        // compare must equal it. The literal is stored UTF-8 (repo default — existing åäö literals
        // compile and round-trip here), and the oracle was taken over exactly those UTF-8 bytes.
        const string password = "Lösenord-åäö";
        // SHA-1 of the UTF-8 bytes of that string (precomputed oracle, not a secret). gitleaks:allow
        const string utf8Prefix = "D4BA9";
        const string utf8Suffix = "6D3AF50D3E947BC566D7F47043FA4CB5928";

        var handler = new CannedHandler(_ => Ok($"{utf8Suffix}:3"));
        var (client, _) = BuildClient(handler);

        var verdict = await client.CheckAsync(password, TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.Breached);
        handler.LastRequestUri.ShouldNotBeNull().AbsolutePath.ShouldBe($"/range/{utf8Prefix}");
    }

    [Fact]
    public async Task CheckAsync_Http500_ReturnsUnavailable_AndLogsHttp5xxReason()
    {
        var (client, log) = BuildClient(new CannedHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.Unavailable);
        log.ShouldHaveSkipReason("http_5xx");
    }

    [Fact]
    public async Task CheckAsync_TimeoutRejected_ReturnsUnavailable_AndLogsTimeoutReason()
    {
        var (client, log) = BuildClient(new ThrowingHandler(new TimeoutRejectedException()));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.Unavailable);
        log.ShouldHaveSkipReason("timeout");
    }

    [Fact]
    public async Task CheckAsync_HttpClientTimeoutBackstop_ReturnsUnavailable_AndLogsTimeoutReason()
    {
        // HttpClient.Timeout surfaces as TaskCanceledException WITHOUT the caller token being
        // cancelled — must be classified as timeout, not rethrown.
        var (client, log) = BuildClient(new ThrowingHandler(new TaskCanceledException()));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.Unavailable);
        log.ShouldHaveSkipReason("timeout");
    }

    [Fact]
    public async Task CheckAsync_NameResolutionFailure_ReturnsUnavailable_AndLogsDnsReason()
    {
        var (client, log) = BuildClient(new ThrowingHandler(
            new HttpRequestException(HttpRequestError.NameResolutionError, "no such host")));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.Unavailable);
        log.ShouldHaveSkipReason("dns");
    }

    [Fact]
    public async Task CheckAsync_NonDnsTransportFailure_ReturnsUnavailable_AndBucketsToHttp5xx()
    {
        // Other transport failures bucket to the nearest bound reason (http_5xx).
        var (client, log) = BuildClient(new ThrowingHandler(
            new HttpRequestException("connection reset")));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.Unavailable);
        log.ShouldHaveSkipReason("http_5xx");
    }

    [Fact]
    public async Task CheckAsync_BrokenCircuit_ReturnsUnavailable_AndLogsCircuitOpenReason()
    {
        var (client, log) = BuildClient(new ThrowingHandler(new BrokenCircuitException()));

        var verdict = await client.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.Unavailable);
        log.ShouldHaveSkipReason("circuit_open");
    }

    [Fact]
    public async Task CheckAsync_CallerCancellation_Throws_InsteadOfFailingOpen()
    {
        var (client, log) = BuildClient(new CannedHandler(_ => Ok($"{Suffix}:1")));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => client.CheckAsync("password", cts.Token));
        log.SkipReasons.ShouldBeEmpty();
    }

    private static (HibpPasswordBreachClient Client, CollectingLogger Log) BuildClient(
        HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://fake.pwned.local/"),
        };
        var log = new CollectingLogger();
        return (new HibpPasswordBreachClient(httpClient, log), log);
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private sealed class CannedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }

    /// <summary>
    /// Captures EventId 5001 skip events. Also guards the CTO log-hygiene invariant: no captured
    /// message may contain password-derived data (asserted via the prefix/suffix constants).
    /// </summary>
    private sealed class CollectingLogger : ILogger<HibpPasswordBreachClient>
    {
        private readonly List<(EventId EventId, string Message)> _events = [];

        public IReadOnlyList<string> SkipReasons =>
            [.. _events.Where(e => e.EventId.Id == 5001)
                .Select(e => e.Message.Split(": ")[^1])];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _events.Add((eventId, formatter(state, exception)));

        public void ShouldHaveSkipReason(string reason)
        {
            SkipReasons.ShouldContain(reason);
            foreach (var (_, message) in _events)
            {
                message.ShouldNotContain(Prefix, Case.Insensitive);
                message.ShouldNotContain(Suffix, Case.Insensitive);
            }
        }
    }
}
