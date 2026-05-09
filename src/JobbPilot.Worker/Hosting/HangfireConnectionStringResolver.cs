using Microsoft.Extensions.Configuration;

namespace JobbPilot.Worker.Hosting;

/// <summary>
/// Resolverar Hangfire-storage-connection-string med fallback-kedja
/// <see cref="PrimaryKey"/> → <see cref="FallbackKey"/> (TD-17 punkt 4).
///
/// I prod sätts <c>ConnectionStrings:HangfireStorage</c> i overlay (eller
/// AWS Secrets Manager via env-var) och pekar mot <c>jobbpilot_worker</c>-
/// rollen med minimal GRANT-set (DML-only på <c>hangfire.*</c>). Api/
/// Persistence använder <c>ConnectionStrings:Postgres</c> som pekar mot
/// <c>jobbpilot_app</c> — lateral access-yta minskar.
///
/// I dev/test räcker <c>ConnectionStrings:Postgres</c> (en sanning lokalt;
/// fallbacken eliminerar split-kostnaden i utvecklingsmiljö).
///
/// Lyft till statisk metod (per STEG 12) för testbarhet — fail-loud-vägen
/// (båda saknas) verifieras isolerat utan host-uppstart.
/// </summary>
public static class HangfireConnectionStringResolver
{
    public const string PrimaryKey = "HangfireStorage";
    public const string FallbackKey = "Postgres";

    public static string Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetConnectionString(PrimaryKey)
            ?? configuration.GetConnectionString(FallbackKey)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{PrimaryKey} eller :{FallbackKey} saknas. " +
                $"Sätt env-var ConnectionStrings__{PrimaryKey} (prod, via Secrets Manager: " +
                $"jobbpilot/<env>/postgres-worker — jobbpilot_worker-roll, GRANT-modell §4) " +
                $"eller ConnectionStrings__{FallbackKey} (dev). " +
                "Se docs/runbooks/hangfire-schema.md §4.");
    }
}
