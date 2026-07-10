using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Jobbliggaren.Infrastructure.Security.BreachCheck;

/// <summary>
/// #616 (CTO-bind) — HIBP Pwned Passwords k-anonymity client. Sends ONLY the first five characters
/// of the password's uppercase SHA-1 hex to <c>GET range/{prefix}</c>; the 35-character suffix is
/// compared locally against the response lines (<c>SUFFIX:COUNT</c>). The <c>Add-Padding</c> header
/// (wired in DI) pads responses to 800–1000 lines where padding lines carry count 0 — those are
/// ignored even on a suffix match. Every transport failure is classified into the CTO-bound
/// fail-open reason set (timeout | http_5xx | dns | circuit_open), logged WITHOUT any
/// credential-derived data (not even the prefix), and returned as <see cref="BreachCheckVerdict.Unavailable"/> —
/// the fail-open policy itself lives in <c>PwnedPasswordValidator</c>, not here.
/// </summary>
internal sealed partial class HibpPasswordBreachClient(
    HttpClient httpClient,
    ILogger<HibpPasswordBreachClient> logger) : IBreachedPasswordChecker
{
    private const int PrefixLength = 5;
    private const int SuffixLength = 35;

    public async Task<BreachCheckVerdict> CheckAsync(string password, CancellationToken cancellationToken)
    {
        // CA5350: SHA-1 is the HIBP range-protocol's k-anonymity requirement (the corpus is keyed
        // by SHA-1), NOT credential storage — password-at-rest hashing remains Identity's PBKDF2.
#pragma warning disable CA5350
        var hex = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
        var prefix = hex[..PrefixLength];
        var suffix = hex[PrefixLength..];

        try
        {
            // ResponseHeadersRead: the padded body is ~40 kB of lines we stream through once.
            using var response = await httpClient.GetAsync(
                $"range/{prefix}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogBreachCheckSkipped(logger, "http_5xx");
                return BreachCheckVerdict.Unavailable;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                // Line shape: 35-char hex suffix, ':', decimal count. Malformed lines are skipped.
                if (line.Length <= SuffixLength + 1 || line[SuffixLength] != ':')
                    continue;

                if (string.Compare(line, 0, suffix, 0, SuffixLength, StringComparison.OrdinalIgnoreCase) != 0)
                    continue;

                // Padding lines carry count 0 and are discarded even on a suffix match; any real
                // occurrence (count >= 1) rejects — no magic threshold above 1 (CTO-bind FORK 3).
                return long.TryParse(line.AsSpan(SuffixLength + 1), out var count) && count >= 1
                    ? BreachCheckVerdict.Breached
                    : BreachCheckVerdict.NotBreached;
            }

            return BreachCheckVerdict.NotBreached;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A genuine caller cancellation is not an HIBP outage — never fail open on it.
            throw;
        }
        catch (BrokenCircuitException)
        {
            LogBreachCheckSkipped(logger, "circuit_open");
            return BreachCheckVerdict.Unavailable;
        }
        catch (TimeoutRejectedException)
        {
            LogBreachCheckSkipped(logger, "timeout");
            return BreachCheckVerdict.Unavailable;
        }
        catch (TaskCanceledException)
        {
            // HttpClient.Timeout backstop (the Polly attempt timeout normally fires first).
            LogBreachCheckSkipped(logger, "timeout");
            return BreachCheckVerdict.Unavailable;
        }
        catch (HttpRequestException ex)
        {
            LogBreachCheckSkipped(
                logger,
                ex.HttpRequestError == HttpRequestError.NameResolutionError ? "dns" : "http_5xx");
            return BreachCheckVerdict.Unavailable;
        }
        catch (IOException)
        {
            // Mid-stream transport drop while reading the body — nearest bound reason bucket.
            LogBreachCheckSkipped(logger, "http_5xx");
            return BreachCheckVerdict.Unavailable;
        }
    }

    // EventId 5001 — the fail-open observability signal (CTO-bind FORK 1). Consumed as a
    // log-derived counter (group on EventId + Reason in Seq): breach_check_skipped_total{reason}.
    // NEVER log the password, hash, prefix, or suffix here — this event is about availability.
    [LoggerMessage(5001, LogLevel.Warning, "Breach-check skipped (fail-open): {Reason}")]
    private static partial void LogBreachCheckSkipped(ILogger logger, string reason);
}
