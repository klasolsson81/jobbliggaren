# Code-review — F6 P5 Punkt 4 `/oversikt`

**Datum:** 2026-05-24
**Agent:** code-reviewer
**AgentId:** `af16f6c15c1b431ca`
**Verdict:** APPROVED med 3 rekommenderade in-block-fixar (M2 + M3 + M4, ~30 min)
**Scope:** 10 nya filer + 2 ändrade (`globals.css`, `app-shell.tsx`)

## Sammanfattning (Severity-räkning)

| Severity | Count | Status |
|---|---|---|
| Block | 0 | — |
| Critical | 0 | — |
| Major | 3 (M2 / M3 / M4) | Rekommenderade in-block-fix |
| Minor | 6 (m1–m6) | Observationer, ingen åtgärd krävs |

Per CLAUDE.md §9.6 + memory `feedback_td_lifting_discipline`: alla Major-fynd inom F6 P5 P4-scope, default = in-block-fix. Ingen TD-lyftning.

## Major (in-block-fixar)

### M2 — Hårdkodade tidstämplar i notiser

Filer: `components/oversikt/oversikt-page.tsx:109, 135, 153, 171, 194`

Tidsstämplar (`"i dag · 07:00"`, `"i dag · 08:12"`, `"i går"`, `"denna vecka"`) är hårdkodade strängar i orkestratorn. Problem:
- `"i dag · 07:00"` blir fel om sidan renderas före 07:00
- `"i går"` på Intervju härleds inte från `recentInterviews[0].updatedAt` trots att datan finns

**Fix (Variant A för data-driven, B för mock-only):** Härled `time` från `latestOffer.updatedAt` + `recentInterviews[0].updatedAt`; flytta resten till `OVERSIKT_MOCK` med `// MOCK BE-port saknas`-kommentar.

### M3 — `savedJobsDeadlines` föråldras

Fil: `lib/oversikt/mock-data.ts:74-77`

Hårdkodade `{ date: "2026-05-25" }` blir "fel" efter 2026-05-31. Fix: filtrera mot `today` i `oversikt-page.tsx` — skippa notisen om alla deadlines passerat.

### M4 — `findRecentInterviews` JSDoc-implementation-divergens

Fil: `lib/oversikt/aggregations.ts:197-205`

JSDoc säger "mindre än 24h gammal" men `daysSince() <= 1` är UTC-kalenderdag-trunkering = upp till ~48h. Fix: uppdatera JSDoc till "inom 1 kalenderdag UTC" + lägg test för 47h-edge.

## Verifierat OK (aktiv kontroll, ingen brist)

- TypeScript-strict: ingen `any`, type guards korrekt (`parseDismissed`)
- RSC/Client-boundary: inga funktioner som props över gränsen, `NoticeData.text: ReactNode` serializable
- `useSyncExternalStore`-mönstret: SSR-säker hydration (server-snapshot = stable `"[]"`)
- localStorage: try/catch (Safari ITP), SSR-guard, ingen PII
- `unauthorized`-redirect korrekt
- CLAUDE.md §5.2: ingen `console.log`/emoji/"!"/gradient i ny TSX
- Svenska §10.3: "du", inga utropstecken, civic-utility-OK
- Datum §10.2: `formatSwedishShortDate` ger "13 maj", `stampDate` ger ISO YYYY-MM-DD
- HANDOVER §9 anti-patterns: ingen "Snooze"/Ärendenr/AI-insikter/snabb-CTA längst ner
- 21 vitest-tester: happy + tom + UTC-DST + duplicates + invalid + framtida
- `force-dynamic` + per-user: korrekt, matchar CTO-dom D2
- App-shell: additivt, brand-link orört (CTO D6 respekterat)

## Minor-observationer (ej åtgärd krävs)

- **m1** Defensiv `[...arr].sort()` — backend-kontrakt anger ej ordning; korrekt disciplin
- **m2** Dismiss-race vid snabb dubbel-klick — funktionella setState skyddar; localStorage kan ha mikrorace men nästa write synkar
- **m3** `() => undefined` istället för `() => {}` — trivial
- **m4** `formatThousands` hand-rullad — `toLocaleString("sv-SE")` ger NBSP; ej brist
- **m5** `ev.time: string` ej validerad — mock-yta, OK
- **m6** `findLatestOffer` tie-break — ej kritiskt

---

*Sparad per CLAUDE.md §9.2. In-block-fixar M2 + M3 + M4 tillämpas innan commit-batch.*
