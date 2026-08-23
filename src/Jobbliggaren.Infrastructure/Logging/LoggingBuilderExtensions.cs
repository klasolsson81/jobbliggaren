using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Logging;

/// <summary>
/// TD-104 / Pre-4 STEG 6 — wires the persistent structured log sink (Seq) onto the
/// existing <see cref="Microsoft.Extensions.Logging"/> pipeline.
///
/// senior-cto-advisor decision (2026-06-14, <c>a459197161c622d4c</c>): Variant B —
/// additive MEL → Seq via <c>Seq.Extensions.Logging</c>; Serilog rejected (YAGNI —
/// backend replacement buys nothing this step needs, and would risk the ASP.NET-in-Worker
/// boundary + a default-destructuring footgun). The MEL <c>ILogger&lt;T&gt;</c> seam is
/// untouched; the console provider keeps writing to stdout.
///
/// Shared by both composition roots (Api + Worker) so the sink config cannot drift between
/// hosts. The package is HTTP-agnostic, so this lives in Infrastructure (referenced by the
/// HTTP-free Worker) without breaking the WorkerLayerTests ASP.NET-free invariant (ADR 0023).
///
/// Config-gated (security-auditor STEG 6 acceptance criterion): the Seq provider attaches
/// ONLY when <c>Seq:ServerUrl</c> is configured. Environments without it stay console-only.
/// Dev sets <c>Seq:ServerUrl=http://localhost:5341</c>. That sink is now loopback-bound AND
/// admin-authenticated (#1198, 2026-08-04) — and both halves are load-bearing, because the
/// old justification "no prod PII" was MEASURED FALSE on 2026-08-04: the sink held 41
/// activation/confirmation links in plaintext plus one real address, since
/// <c>ConsoleEmailSender</c> logged the whole body for any recipient at all. Since #1208 it
/// does so only for an RFC 2606/6761-reserved one. Prod sets it via env/secret to the
/// self-hosted EU Seq (ADR 0050; the sink itself is unbuilt and owned by #1175).
/// <c>ServerUrl</c> + any <c>ApiKey</c> are env/secret, never committed.
/// </summary>
public static class LoggingBuilderExtensions
{
    public const string SeqSectionName = "Seq";

    public static ILoggingBuilder AddJobbliggarenLogging(
        this ILoggingBuilder logging,
        IConfiguration configuration)
    {
        var seq = configuration.GetSection(SeqSectionName);

        // Config-gated: no ServerUrl → no Seq sink (console-only fallback preserved).
        if (!string.IsNullOrWhiteSpace(seq["ServerUrl"]))
        {
            // AddSeq(IConfigurationSection) reads ServerUrl, ApiKey, MinimumLevel and
            // per-category level overrides from the Seq section. Missing keys fall back
            // to the provider defaults.
            logging.AddSeq(seq);
        }

        return logging;
    }
}
