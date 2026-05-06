# adr-keeper — Pre-commit rapport Turn 4

**Datum:** 2026-05-06

## Findings-sammanfattning
- Blocking: 0
- Non-blocking: 6
- Total: 6

## Blocking findings

Inga.

## Non-blocking findings

### NB-1 — Metadata-block format avviker från etablerat mönster (båda ADR:erna)

ADR 0017 och ADR 0018 använder en lista-syntax för metadata:

```markdown
- **Status:** Accepted
- **Date:** 2026-05-06
- **Deciders:** Klas Olsson
- **Related:** …
```

Samtliga ADR 0001–0016 använder ett fristående block utan listpunkter och med svenska fältnamn:

```markdown
**Datum:** 2026-05-06
**Status:** Accepted
**Beslutsfattare:** Klas Olsson
**Relaterad:** …
```

ADR 0015 och 0016 (skrivna samma dag) har exakt detta format. Avvikelsen är ofarlig men skapar inkonsekvens i index-scanning och adr-keeper:s templating. Fältnamnen `Date`, `Deciders`, `Related` är engelska; övriga ADR:er använder `Datum`, `Beslutsfattare`, `Relaterad`.

**Referens:** `0017-frontend-auth-pattern.md` rad 3–6; `0018-cookie-and-csrf-strategy.md` rad 3–5.

---

### NB-2 — Titelseparator `:` istället för ` — ` (båda ADR:erna)

ADR 0017: `# ADR 0017: Frontend Authentication Pattern …`
ADR 0018: `# ADR 0018: Cookie and CSRF Strategy …`

Alla ADR 0001–0016 använder `# ADR NNNN — Titel` (em-dash med mellanslag). Kolon-separatorn avviker från mall och README-index-rendered jämförelse.

**Referens:** `0017-frontend-auth-pattern.md` rad 1; `0018-cookie-and-csrf-strategy.md` rad 1.

---

### NB-3 — Sektionsrubriker på engelska (båda ADR:erna)

ADR 0017 och 0018 använder `## Context`, `## Decision`, `## Considered Alternatives`, `## Consequences`, `## References`. ADR 0001–0016 (inklusive 0015 och 0016 från samma dag) använder `## Kontext`, `## Beslut`, `## Alternativ som övervägdes`, `## Konsekvenser`, `## Referenser`.

Tekniska termer i body-text är oförändrade (engelska). Avvikelsen gäller enbart rubriknivån H2.

**Referens:** Båda ADR-filerna, samtliga H2-rubriker.

---

### NB-4 — Audit event-koder (1001/1002/1003) saknas i ADR 0017

Review-scopet begärde verifiering av om `## Log and Audit Policy` beskriver audit events 1001/1002/1003. Sektionen är välskriven och täcker SessionId-hantering, Redis SHA-256-nycklar och PII-principer, men nämner inga specifika event-koder. Om event-koderna är implementerade i koden och anses vara dokumentationsvärdiga bör de adderas i en ny version av ADR 0017 (eller som separat runbook). Om de inte är implementerade ännu är avsaknaden korrekt.

**Referens:** `0017-frontend-auth-pattern.md` rad 73–91 (`## Log and Audit Policy`).

---

### NB-5 — "Active-sessions UI" saknas i `## Out of Scope` (ADR 0017)

Review-scopet begärde verifiering av att "Active-sessions UI" ingår i `## Out of Scope`-sektionen (uppdaterad i P2.1 enligt scopet). Det gör det inte. Sektionen innehåller OAuth, MFA, Rate-limiting, Password reset, Email verification, "Remember me", Secondary user-sessions index och JTI value-object migration — men inte Active-sessions UI. Om detta är ett planerat UI-element för Fas 1 bör det adderas i `## Out of Scope`.

**Referens:** `0017-frontend-auth-pattern.md` rad 114–131 (`## Out of Scope (Deferred)`).

---

### NB-6 — Ingen `## Performance Budget`-sektion och ingen `/auth/refresh` 410-notering (ADR 0017)

Review-scopet frågade om en `## Performance Budget`-sektion med p50/p99-siffror existerar. Den gör det inte. ADR 0017 nämner Redis-latens informellt (`~5-15ms`) i `### Negative`-stycket men har ingen dedikerad sektion. Likaså saknas en explicit notering om att `/auth/refresh` returnerar 410 (vilket nämns i review-scopet som en förväntad sektion `## Deprecated Endpoints` eller likvärdig).

Om dessa data existerar i koden eller integrationstesterna bör de formaliseras i ADR:n. Om de inte ännu är fastslagna är avsaknaden motiverad — men bör noteras för Fas 1.

**Referens:** `0017-frontend-auth-pattern.md` rad 103–108 (`### Negative`).

---

## Verified OK

- **ADR 0017 `Related`-fält refererar ADR 0018** — korrekt nummer, korrekt rubrikform.
- **ADR 0018 `Related`-fält refererar ADR 0017** — reciprok korrekt. Cross-reference-symmetri uppfylld.
- **ADR 0018 `### Backend trust model`** — sektionen är på plats, placerad _före_ `### CSRF mitigation`, rubriken saknar bindestreck (`trust model`, inte `trust-model`). Korrekt.
- **ADR 0018 fyra bullet-punkter och Invariant-klausul** — samtliga fyra punkter (`browser never issues...`, `All cookie management...`, `The backend receives...`, `The backend validates...`) och `**Invariant:**`-klausulen är på plats.
- **ADR 0018 inline-referens till ADR 0017** — `per ADR 0017` i `### Architecture`-stycket är korrekt.
- **ADR 0018 `### CSRF mitigation`** — tre lager dokumenterade (SameSite=Strict, Origin/Host, No state changes on GET). Konsistent med beslutet.
- **README.md ADR-index** — 0017 och 0018 listade med korrekta titlar, status Accepted, datum 2026-05-06. Inga saknade rader, inga felaktiga statusar.
- **ADR 0017 `## Considered Alternatives`** — fyra alternativ (Auth.js, Better Auth, Stateless JWT, localStorage) med motiverade avvisanden.
- **ADR 0018 `## Considered Alternatives`** — tre alternativ (Cross-origin cookie, SameSite=Lax + double-submit, Stateless JWT) med motiverade avvisanden; varav ett delegerar till ADR 0017 (korrekt).
- **Inga H4/H5-rubriker** — varken ADR 0017 eller 0018 innehåller H4 eller H5. Djupaste nivå är H3, konsistent med 0015 och 0016.
- **Inget `Kontext`-fält i metadata** — ADR 0015 och 0016 har ett `**Kontext:**`-fält; 0017 och 0018 saknar det. Detta är en lindrig inkonsistens (se NB-1) men inte fristående blocking.

---

## Verdict

**APPROVE WITH NOTES**

ADR 0017 och 0018 är substantiellt korrekta. Besluten är välmotiverade, alternativ är dokumenterade, cross-references är symmetriska, och ADR-indexet är korrekt. `### Backend trust model` i ADR 0018 är korrekt placerad och formulerad.

De sex non-blocking fynden handlar om fyra typer av avvikelse:
1. Formatavvikelse (NB-1, NB-2, NB-3) — enhetlighet gentemot ADR 0001–0016. Rekommenderas åtgärdat vid nästa ADR-redigering.
2. Innehåll som review-scopet förväntade men som inte finns (NB-4, NB-5, NB-6) — antingen är innehållet inte implementerat ännu (korrekt avsaknad) eller behöver efterdokumenteras när det implementeras.

Inga blocking fynd. Filerna är godkända för commit.
