using System.Text;
using Jobbliggaren.QA.Corpus.Generation;

namespace Jobbliggaren.QA.Corpus.Reporting;

/// <summary>Per-stratum derivation hit-rate (observe-only — CTO Fork 6).</summary>
public sealed record DeriverStratumStat(CorpusStratum Stratum, int Total, int Exact, int Stemmed, int Empty);

/// <summary>Per-criterion review verdict distribution (observe-only — CTO Fork 6).</summary>
public sealed record ReviewerCriterionStat(string CriterionId, int Pass, int Warn, int Fail, int NotAssessed);

/// <summary>All metrics + headline findings the harness measured over one corpus run.</summary>
public sealed record FindingsReportData(
    int Seed,
    int Scale,
    string RubricVersion,
    int SavedSearchesCreated,
    int DeriverTotal,
    int DeriverCrashes,
    int ReviewerTotal,
    int ReviewerCrashes,
    int FakePnrCases,
    int NonB4PnrEchoCases,
    IReadOnlyList<DeriverStratumStat> Deriver,
    IReadOnlyList<ReviewerCriterionStat> Reviewer);

/// <summary>
/// Builds the deterministic findings-report artifact (Fas 4 STEG C, CTO Fork 6 = 6C). The
/// two MUST-gates (bearing invariant + crash-safety) lead as rows 1–2 so a breach can never
/// drown in statistics ("fail hard + flag at top"); everything else is observe-only (per CTO
/// Fork 6 / CLAUDE.md §2.5 — fitness functions stay observe-only until a Klas ratchet). Pure
/// string assembly: deterministic, no clock, never echoes a raw personnummer (counts only).
/// The report's value is realised when its "top-miss / thin-tier" findings feed a hardening STEG.
/// </summary>
public static class FindingsReport
{
    public static string Build(FindingsReportData d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var sb = new StringBuilder();
        void Line(string s) => sb.AppendLine(s);
        void LineI(FormattableString fs) => sb.AppendLine(FormattableString.Invariant(fs));

        Line("# Jobbliggaren — Motor-stresstest findings (Fas 4 STEG C)");
        Line("");
        Line("> Deterministic QA harness (ADR 0071 — NO AI/LLM). Machine-generated; regenerate via");
        Line("> `dotnet test`. NOT committed (gitignored).");
        LineI($"> Seed=`0x{d.Seed:X}` · Scale=`{d.Scale}` · Rubric=`{d.RubricVersion}`.");
        Line("");

        // ── Row 1: the bearing invariant (fail hard + flag at top) ──────────
        var invariantOk = d.SavedSearchesCreated == 0;
        Line("## 1. BEARING INVARIANT — ADR 0040 Beslut 4 (GATING)");
        LineI($"**{(invariantOk ? "OK" : "BROKEN")}** — {d.SavedSearchesCreated} SavedSearch created by derivation across the whole corpus (requires 0; derivation PROPOSES, never creates a search without explicit user confirmation).");
        Line("");

        // ── Row 2: crash-safety (MUST = 100%) ───────────────────────────────
        var crashOk = d.DeriverCrashes == 0 && d.ReviewerCrashes == 0;
        Line("## 2. CRASH-SAFETY — MUST be 100% (GATING)");
        LineI($"**{(crashOk ? "OK" : "BROKEN")}** — deriver {d.DeriverTotal - d.DeriverCrashes}/{d.DeriverTotal} crash-free; reviewer {d.ReviewerTotal - d.ReviewerCrashes}/{d.ReviewerTotal} crash-free (both profiles).");
        Line("");

        // ── Headline finding ────────────────────────────────────────────────
        Line("## 3. HEADLINE FINDING — personnummer echoed in non-B4 review evidence");
        LineI($"Of {d.FakePnrCases} fake-personnummer CVs, **{d.NonB4PnrEchoCases}** had a personnummer echoed in a non-B4 criterion's `TextSpanEvidence` (A1/A2/A6/A8 cite spans of profile/experience text the user placed the pnr in). B4 itself stays PII-safe (count-only `StructuralEvidence`).");
        Line("Not an active leak today (review is on-demand for the owner; the engine has no logger). BINDING");
        Line("obligation (security-auditor): STEG B — and any persist/cache/TD-111-retention STEG touching review");
        Line("output — MUST redact pnr from ALL `TextSpanEvidence.Quote` (via `Personnummer.Masked`) before any");
        Line("log/cache/persist/transmit of a `CvReviewResult`, with mandatory security-auditor re-review.");
        Line("");

        // ── Observe-only: deriver hit-rate per stratum ──────────────────────
        Line("## 4. Deriver — hit-rate per stratum (observe-only)");
        Line("| Stratum | N | Exact | Stemmed | Empty |");
        Line("|---|--:|--:|--:|--:|");
        foreach (var s in d.Deriver)
            LineI($"| {s.Stratum} | {s.Total} | {s.Exact} | {s.Stemmed} | {s.Empty} |");
        Line("");

        // ── Observe-only: reviewer verdict distribution per criterion ───────
        Line("## 5. Reviewer — verdict distribution per criterion, ATS profile (observe-only)");
        Line("| Criterion | Pass | Warn | Fail | NotAssessed |");
        Line("|---|--:|--:|--:|--:|");
        foreach (var c in d.Reviewer)
            LineI($"| {c.CriterionId} | {c.Pass} | {c.Warn} | {c.Fail} | {c.NotAssessed} |");
        Line("");

        // ── Thin tier: criteria that never get a definite (assessed) verdict ─
        var thin = d.Reviewer.Where(c => c.Pass + c.Warn + c.Fail == 0).Select(c => c.CriterionId).ToList();
        Line("## 6. Thin tier — criteria with no assessed verdict across the corpus (observe-only)");
        Line(thin.Count == 0
            ? "None — every reviewed criterion produced at least one assessed verdict."
            : "`" + string.Join("`, `", thin) + "` — never assessed across the corpus (candidate rule gaps / always-NotAssessed-v1).");
        Line("");

        return sb.ToString();
    }
}
