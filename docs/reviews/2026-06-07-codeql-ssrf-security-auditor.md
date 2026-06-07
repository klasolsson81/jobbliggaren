# Security-audit: CodeQL SSRF-fix (`js/request-forgery`) — `fix/codeql-ssrf-jobadid-validation`

**Agent:** security-auditor (agentId `a847cc321367d4ab3`)
**Status:** CONDITIONAL → **uppfyllt** (Major-syskon in-block-fixade i samma PR)
**Datum:** 2026-06-07
**Auktoritet:** OWASP ASVS V5, CLAUDE.md §5.4, Saltzer/Schroeder fail-safe defaults

## Praise
- `guid.ts` korrekt allowlist + tydlig motivering (input-restriction = CodeQL:s egen remediation).
- DRY-konsolidering av `GUID_REGEX` mot zod-scheman — en sanningskälla.
- Defense-in-depth korrekt skiktad (guard FÖRE URL + encode PÅ segment).
- Fail-safe-shape korrekt per kontrakt (ApiResult `kind` / ActionResult `success`).
- Ingen läcka: catch-block returnerar opaka fel-shapes; intern `BACKEND_URL`/host exponeras aldrig.

## Major (täckningsgap — CTO-mandat: bred sökning efter syskon-call-sites)
Tre oskyddade call-sites med identiskt rå id-interpolations-gap, samma sårbarhetsklass, samma fas/PR-scope (§9.6 → in-block, ej TD):
1. `src/lib/api/applications.ts:79` — `getApplicationById`
2. `src/lib/api/job-ad-status.ts:55` — `hasAppliedJobAd`
3. `src/lib/api/resumes.ts:56` — `getResumeById`

**→ Alla tre in-block-fixade i denna PR** med samma etablerade mönster (`isValidId`-guard → funktionens fail-shape + `encodeURIComponent`). Auditorn: "Re-review behövs inte om Major-call-sitesarna fixas med exakt samma mönster."

## Minor (hygien)
1. `src/lib/actions/resumes.ts:26` — duplicerad `GUID_REGEX` → **fixad** (importerar nu delad modul).
2. `src/lib/actions/resumes.ts` — guard utan encode → **fixad** (encode tillagd på alla sites, defense-in-depth-paritet).
3. `src/lib/api/job-ads.ts:97` — encode utan guard → **verifierad säker** (encode neutraliserar path-aktiva tecken); konsekvens-polish, ej gap.

## GDPR
Ingen PII-konsekvens. Ren input-validering + output-encoding på GUID-segment. Inga nya PII-fält, ingen ändrad loggning, ingen retention/consent/transfer-påverkan.

## Dom
**CONDITIONAL** vid granskningstillfället; **villkoret uppfyllt** — de 3 Major-syskon-call-sitesarna + Minor-hygienen in-block-fixade i samma PR. Re-scan-verifiering av de 8 alarmen sker separat på PR:n (utanför auditorns scope).
