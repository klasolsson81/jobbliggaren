# ADR-keeper-rapport — ADR 0026 (ALB HTTP-only Fas 0)

**Datum:** 2026-05-09
**Granskare:** adr-keeper
**Scope:** `docs/decisions/0026-alb-http-only-fas0.md` + `docs/decisions/README.md` rad för 0026
**Verdict:** Godkänd med en justering rekommenderad

## Verifierat OK

- **Header-struktur** matchar ADR 0001/0019/0024/0025 — Datum, Status, Kontext, Beslutsfattare, Relaterad. Status `Accepted` korrekt (Klas gav GO, Alt B explicit).
- **Sektioner** kompletta: Kontext / Beslut / Konsekvenser (positiva + negativa + mitigering) / Alternativ övervägda / Implementations-status / Validering. Inline-Alt-B i Kontext + Alt A/C separat följer 0025:s mönster.
- **Tidsfönster + triggers** är konkreta och objektivt verifierbara: datum 2026-06-08 (kalender), Trigger 1 (Terraform-state-flagga), Trigger 2 (24h-fönster vid första icke-Klas-konto), Trigger 3 (kalender), Trigger 5 (Fas 2-gate). Trigger 4 (säkerhetsincident) är medvetet subjektiv och flaggad så i Mitigering — acceptabelt.
- **Mitigation-stack 6 punkter** är spårbara: rate-limiting verifierad i `src/JobbPilot.Api/RateLimiting/`, IP-anonymisering i ADR 0024 D7, audit-cascade i ADR 0024 D3-D6, CloudTrail i `infra/terraform/modules/cloudtrail/`, DenyInsecureTransport i `bootstrap/main.tf`, ALB-DNS-disciplin är policy (icke-kod, korrekt taggad "obfuscation, icke-säkerhet").
- **ADR-index** rad 40 inlagd korrekt mellan 0025 och Planerade ADRs — format-konsistent med 0024/0025-raderna (kolumnbredder, länk-format, datum YYYY-MM-DD).

## Justeringsförslag

**1. Variabelnamn-mismatch (faktisk felaktighet).** ADR refererar `var.alb_https_enabled` på rad 54, 61, 85, 95, 135. Koden i `infra/terraform/modules/alb/variables.tf:69` heter `https_listener_enabled`. Vid supersession kommer någon söka efter `alb_https_enabled` och inte hitta något. Bör korrigeras till `var.https_listener_enabled` på alla fem platser. Detta är inte en innehålls-glidning utan en kod-spårbarhets-bugg.

**2. ADR 0007 (branch protection)** kan med fördel listas i Relaterad — Trigger 5:s Fas 2-koppling vilar på samma "Fas 0-ramverk" som 0007/0019. Inte kritiskt; nice-to-have.

## Cross-ref-rekommendationer

ADR 0026 är en *ny* acceptans-ADR på samma mönster som 0025 — inte en supersession. Befintliga ADRs (0017, 0024, 0025) skapades innan 0026 och behöver inte uppdateras retroaktivt. ADR-konventionen i projektet håller relationer envägs framåt (nya ADR:er listar gamla i Relaterad, inte tvärtom) — se 0024 → 0008/0009/0010 utan att de uppdaterades. **Rekommendation: kvarstår envägs.** Undantag: vid supersede ska föregångarens status-fält uppdateras, men 0026 supersederar ingen.
