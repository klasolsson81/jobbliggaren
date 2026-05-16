---
session: F2 samlad — ingestion payload-trunkerings-fix (hybrid) + sök-yta-omdesign B–E
datum: 2026-05-16
slug: f2-ingestion-hybrid-and-search-redesign
status: PÅGÅENDE — Batch 1 Part 1 + ADR-amendment klart, pending deploy (STOPP 3)
commits:
  - "feat(jobads): Batch 1 Part 1 — snapshot-trunkerings-resiliens (denna session)"
  - "docs(ADR 0032): amendment 2026-05-16 hybrid + Batch 1 docs-synk (denna session)"
---

# F2 samlad session — ingestion-hybrid-fix + sök-omdesign

6 linjära batchar, 7 Klas-STOPP. Denna logg uppdateras löpande per batch
(§1.5) — överlever kompaktering.

## Mål

Stäng F2-milstolpen: (1) fixa ingestion payload-trunkering så korpus
konvergerar mot ~40k+, (2) sök-yta-omdesign B–E (SearchCriteria multi,
relevans, IsNew, typeahead) + frontend.

## Batch 0 — Discovery (ingen kod)

CloudWatch `/aws/ecs/jobbpilot-dev/worker` (dev `v0.2.8-dev`, 48h) verifierade
rotorsak: `/v2/snapshot` >364 MB singel-GET termineras **icke-deterministiskt**
mid-stream (START→TRUNC 87–442 s, bytePos 21–364 MB) → ofångad
`System.Text.Json.JsonException` vid enumeration i `PlatsbankenJobSource`
(saknade try/catch runt `await foreach`) → propagerade förbi
`SyncPlatsbankenSnapshotJob`s per-item-upsert-catch → `Hangfire.AutomaticRetry`-
storm (60 starts/0 completes). Hypoteser **motbevisade**: HttpClient.Timeout=5min
(ingen tidsvägg), MaxResponseContentBufferSize=500MB (364<500 + streaming
bypassar), Polly (completar vid headers-read). Sök-kod kartlagd för Batch 3–5.

## Batch 0→1 — CTO + architect

- **senior-cto-advisor** (`a237dfe175089fb7d` → forts. `ad8564aafc29be5a0`):
  först A2 (stream-cursor), sedan **omvägd till hybrid** efter web-verify
  (JobTech GettingStartedJobStreamSE.md 2026-05-16: full-korpus-pattern är
  snapshot-först + stream; **ingen dokumenterad stream-only-backfill**;
  retention-djup okänt; rate-limit 1/min, granularitet ospecificerad). A2:s
  premiss rev → §9.5. MA-triage: 1.1=A (stateless, ingen cursor), 2.1=A
  (behåll job/id, ändra internals), 3.1=A (enumeration-catch + bounded retry),
  4.1=A (delad limiter). Snapshot-paus rekommenderad (prio-1).
- **dotnet-architect** (`a6a02546f13bd5236`): design-skiss INNAN kod,
  bekräftade lager-placering + ACL-bevarat kontrakt; surfade 4 MA-punkter +
  drift-konvergens-STOPP-kandidat.

## Klas-STOPP 1 (GO)

A2-inriktning (sedermera hybrid via web-verify), snapshot-paus nu
(CC levererade operatörsprocedur: Worker ECS desired-count→0, ej CC-verkställt),
non-stop-arbetsflöde bekräftat.

## Batch 1 Part 1 — root-cause-fix (levererad)

`PlatsbankenJobSource.FetchSnapshotAsync`: resilient enumeration (manuell
enumerator, enumeration-boundary-catch `JsonException`/`IOException`/
`HttpRequestException` **skild** från per-item-upsert-catch, `OCE` rethrow,
`MaxSnapshotAttempts=3` bounded retry idempotent via UNIQUE-index, graceful
`yield break` → ingen storm). LoggerMessage 5004/5005. Regressionstest FÖRST
(WireMock trunkerad-body, reproducerar exakt rotorsaken). Build 0/0; svit
**1043 grön** (Domain 293/App 398/Arch 51/Api.Int 269 (+1)/Worker 26/Migrate 6).
**code-reviewer** `ab3fefc83d7e4f22a` GO (0 Block/0 Major/1 Minor — redundant
`using` fixad in-block §9.6).

## Klas-STOPP 2 (GO)

Drift = **recurring inkrementell, ingen timeout-höjning** (CTO-lutning).
Amendment-hantering = **CC drafter** (medvetet Klas-override av §9.4
verbatim-text-källa — dokumenterat i amendment-Status). ADR 0032-amendment
2026-05-16 **Accepted** (hybrid + MA + drift + konvergens-risk). README-rad
uppdaterad.

**Syntes:** hybrid + alla MA=A + stream "oförändrat mönster" ⟹ Part 1 ÄR hela
Batch 1-kodändringen. Ingen separat Part 2 (windowed-stream-katch-up tillhörde
förkastad ren A2; bevaras som framtida skala-trigger, ej TD — §9.6/§9.7).

## Beslut & avvägningar

- Konvergens-risk medvetet accepterad (Klas 2026-05-16): ~40k+ tar dygn ej
  timmar; STOPP 3 mäter korpus-**tillväxt över tid**, ej omedelbar ~40k.
- §3 förtydligas, supersederas ej (hybrid bevarar overlap-window-mönstret).
- §9 X4 410 + `JobType:"snapshot"`-literal + ADR 0036-metric oförändrade
  (MA 2.1=A).

## Nästa

- Tag-push `v0.2.x-dev` (Klas-op) → **STOPP 3 cron-grön** (hård F2-DoD-gate;
  Batch 3 startar EJ innan grön).
- Batch 2 (ADR 0042 + ADR 0039 Beslut 3-supersession, STOPP 4) → Batch 3–6.
- Fas 2 formell stängning vid B–E komplett (samlad).
