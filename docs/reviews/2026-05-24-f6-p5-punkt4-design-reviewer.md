# Design-review — F6 P5 Punkt 4 `/oversikt`

**Datum:** 2026-05-24
**Agent:** design-reviewer
**AgentId:** `a5927ef8836d080f0`
**Verdict:** NEEDS_REWORK (3 Major + 2 in-block Minor)
**Manifest-respekt:** FAS-DEFERRAL-MANIFEST respekterad — DEFERRED-poster flaggas bara som FYI.

## Severity-räkning

| Severity | Count | Status |
|---|---|---|
| Block | 0 | — |
| Major | 3 (M1 + M2 + M3) | Alla in-block-fixade i samma commit |
| Minor | 2 (m1 + m3) | In-block-fix |
| FYI | 6 | — |
| Praise | 9 | — |

## Major (in-block-fixar)

### M1 — HeaderStats kvar i header på `/oversikt` skapar dubblering med Sammanfattning

`HeaderStats` (aktiva annons-räknare + delta-pill från `e0911a3`) visar samma siffra som Sammanfattning > Bevakning > "Aktiva annonser totalt". Target-PNG visar header UTAN HeaderStats på `/oversikt`. Flödesbegriplighet (Area 5 — Norman/Krug): samma data två gånger med olika behandling tvingar användaren att skanna fram auktoritet.

**Fix (Variant A):** Route-villkorlig render i `app-shell.tsx` — dölj `HeaderStats` när `pathname === "/oversikt"`. Mekaniskt entydigt; Variant C (medveten redundans + ADR) bryter HANDOVER §9 (KPI-kort-yta). Beslut: CC tar Variant A direkt, flaggar till Klas som STOPP-fråga för bekräftelse.

### M2 — `activeJobAdsTotal=0` vid endpoint-failure rendras som `"0"` istället för `"—"`

Vid `jobAds.kind !== "ok"` faller `activeJobAdsTotal` till `0` → rendras som mono-`"0"`. Användaren får inte veta om det är genuint noll eller endpoint-failure. Civic-utility-trovärdighet kräver tydlig saknad-state.

**Fix:** Propa som `number | null`; rendera `"—"` vid null. Samma för `savedJobsCount`, `recentSearchesCount`, `cvCount`, `counts.*`.

### M3 — Mock-företagsnamn ("Bonnier News", "Folksam IT") presenteras som fakta när BE-driven `findLatestOffer/RecentInterviews` har riktig data

`ApplicationDto.jobAd?.company` finns — koden använder mock istället. Användaren ser "Bonnier News" trots erbjudande från Skatteverket. **Civic-utility-trovärdighet-fel.**

**Fix:** Använd `latestOffer.jobAd?.company`/`.title` + `recentInterviews[0].jobAd?.company` direkt. Fallback till generisk svensk text om JobAd saknas (manuell ansökan). Behåll `deadlineCopy`/`dateCopy` som dokumenterad mock (BE-port saknas).

## Minor (in-block-fixar)

### m1 — `style={{ marginTop: 40 }}` inline på Summary-section

Token-disciplin (DESIGN.md): undvik inline-style för spacing. Fix: ta bort — `.jp-section`-marginen räcker, eller flytta till modifier-klass.

### m3 — `"X dagar sedan"` rendrerar `"0 dagar sedan"` vid samma-dag-offer

Fix: använd `formatDaysAgo()` (redan implementerad i `aggregations.ts` per code-reviewer M2).

## FYI (deferred-aware, flaggas ej som Major/Block)

- Default-route-byte (CTO-dom D6) — DEFERRED, brand-link orört bekräftat
- DESIGN.md §X.Y Översikt — DEFERRED till same commit som default-route
- ADR för default-route — DEFERRED
- Google Calendar / matching / saved-job-deadline / letters mock — DEFERRED, väl-isolerade i `OVERSIKT_MOCK`
- Notification-server-action — DEFERRED, `useSyncExternalStore` + localStorage korrekt
- `(app)/layout.tsx` parallell nav-array — discovery rekommenderas men troligen ej parallell källa

## Praise (utvalda)

- Verbatim-CSS-klon från v3-källan rad 1461-1879 (ADR 0052-disciplin)
- Server Component default; bara `notice-list.tsx`/`notice-row.tsx` client (motiverat)
- A11y per HANDOVER §6: `<li>` notice, separat fokuserbara CTA + dismiss, summary `<Link>` istället för `<button>` (semantiskt starkare för nav)
- Civic-utility 100%: inga emojis, inga "!", "du" konsekvent, mono för datum/ID/tid, lucide endast funktionellt
- HANDOVER §9 anti-patterns alla undvikna
- Degraderad RSC-fallback strategi (aldrig blank sida)
- `useSyncExternalStore` istället för `useEffect+useState` för localStorage
- Mock centralised i `OVERSIKT_MOCK` med TODO-kommentarer

## Visual-verify-flagga till Klas

Review gjord mot kod + statiska target-PNG. Live `pnpm visual-verify` mot rendered light + dark `/oversikt` ej körd (creds utanför repot). Klas spot-check rekommenderad efter M1-M3 fixade.

---

*Sparad per CLAUDE.md §9.2. M1 + M2 + M3 + m1 + m3 hanteras in-block. M1 flaggas också till Klas för bekräftelse av Variant A vs medveten redundans.*
