# ADR 0047 — design-reviewer-mandat utökat: task-completion/flödesbegriplighet utöver estetik/tokens/a11y

**Datum:** 2026-05-17
**Status:** Accepted
**Kontext:** FAS 3-stängning. Klas live-verifierade `/ansokningar/[id]` (Application-pipeline-detaljvyn) på v0.2.13-dev efter att hela gate-kedjan godkänt — och fann 5 allvarliga UX-flödesdefekter som ingen gate strukturellt kunde fånga. senior-cto-advisor fastställde rotorsaken som en strukturell granskningslucka mot CLAUDE.md §1 civic-utility-premissen, inte en batch-miss.
**Beslutsfattare:** senior-cto-advisor (agentId ae3297a7612835966 — rotorsaks-fastställning, mandat-utökning vs ny-agent-triage, källförankring 2026-05-17); Klas Olsson (GO givet 2026-05-17 i chatten: "GO — ADR + mandat-utökning"; Accepted-flip explicit); Claude Code (ADR-leverans + agent-fil-utökning denna session)
**Relaterad:** ADR 0044 (gate-disciplin-mönster — icke-regression-ratchet som denna efterliknar i gate-täckningsanda), ADR 0046 (FAS 3-scope — kontexten där defekten upptäcktes; design-reviewer-VETO-villkoret), ADR 0003 (design as skills — design-reviewer-auktoritetskälla), ADR 0016 (civic design language som arkitekturkrav), ADR 0037/0038 (designsystem v2 / GOV.UK-läsbarhetsgolv). Relaterade: CLAUDE.md §1 (civic-utility-identitet) / §8 punkt 6 (DoD a11y/begriplighet) / §9.2 (agent-invocation-disciplin), DESIGN.md, `.claude/skills/jobbpilot-design-*`, `.claude/agents/design-reviewer.md`.

---

## Kontext

Vid FAS 3-stängning live-verifierade Klas `/ansokningar/[id]` (Application-pipeline-detaljvyn) på v0.2.13-dev. Vyn hade då passerat hela gate-kedjan:

- **code-reviewer** — GO (0 Blocker / 0 Major / 0 Minor)
- **security-auditor** — GO
- **design-reviewer** — APPROVED via rendered-screenshot-VETO (0 Blocker / 0 Major / 1 Minor)

Trots tre GO fann Klas **5 allvarliga UX-flödesdefekter**:

1. Status och action blandades utan nuläges-förankring — användaren ser inte var i pipelinen ansökan är innan en kontroll erbjuds.
2. Ett irreversibelt utfall kunde registreras utan att konsekvensen kommunicerades före handlingen.
3. Etikettlös `{outcome}—{note}`-konkatenering — rådata renderad utan fält-etiketter.
4. Två separata formulär var visuellt sammanflätade — användaren kunde inte se var det ena slutade och det andra började.
5. Ingen sektionering — Klas: "allt rakt upp och ner som ett rörigt formulär", "jag fattar ingenting".

senior-cto-advisor (agentId ae3297a7612835966) fastställde rotorsaken som en **strukturell granskningslucka**, inte en batch-miss av en enskild gate:

- **code-reviewer** granskar arkitektur och CLAUDE.md-efterlevnad.
- **security-auditor** granskar PII, auth och secrets.
- **design-reviewer** granskar design-tokens, a11y (WCAG 2.1 AA) och civic-utility-**estetik** via rendered screenshots.

**Ingen** agent hade mandat att granska *uppgiftsbegriplighet* — kärnan i CLAUDE.md §1 civic-utility-premissen: enkelt, svårt att göra fel, enkelt att förstå, tydligt flöde. design-reviewer tittade på hur vyn **såg ut**, inte hur det **var att genomföra en uppgift** i den. En gate som strukturellt inte kan fånga §1-brott ljuger om sin täckning — samma gate-täckningsanda som ADR 0044 (en coverage-gate som inte mäter regressionen den påstår skydda mot är en lögn om sin täckning).

Krafter som spelar in:

- **Gate-ärlighet > gate-antal.** Tre GO på en vy med 5 allvarliga flödesdefekter visade att kedjan hade ett strukturellt blint hål, inte ett kvalitetsproblem i en enskild körning. Att lägga till en fjärde gate utan att fylla luckan hade inte hjälpt.
- **Anti-bloat-doktrin (roster-gap-CTO 2026-05-17 §1.2).** En ny dedikerad UX-agent hade dubblerat design-reviewers screenshot-infrastruktur och introducerat en agent-gräns där ingen sakgräns finns. Samma logik som att CTO+architect bär infra-granskning utan dedikerad infra-agent (ADR 0036-precedensen, kodifierad i CLAUDE.md §9.2).
- **Estetik och begriplighet bor på samma yta.** Den agent som redan tittar på rendered screenshots i light+dark är närmast att också bedöma om uppgiften går att genomföra. Att splittra "ser ut" och "är att använda" på två agenter splittrar mot en sakgräns som inte finns.
- **Rendered screenshot räcker inte ensam.** Flera av defekterna (irreversibelt utfall utan konsekvens-kommunikation, två sammanflätade formulär) avslöjas först när interaktionssökvägen — inte bara den statiska bilden — granskas.

## Beslut

> Beslut fattat av senior-cto-advisor (agentId ae3297a7612835966), Klas-GO 2026-05-17 ("GO — ADR + mandat-utökning"). Status **Accepted** — explicit Klas-GO.

### Beslut 1 — Mandat-utökning, ingen ny agent

**design-reviewer-mandatet** utökades med en **task-completion/flödesbegriplighets-granskning**. Ingen ny agent skapades (anti-bloat-doktrin, roster-gap-CTO 2026-05-17 §1.2). Det befintliga estetik-/tokens-/a11y-mandatet lämnades **oförändrat** — flödes-granskningen är ett **additivt femte granskningsområde**.

### Beslut 2 — Granskningen körs mot rendered screenshots OCH interaktionssökväg

Flödesbegriplighets-granskningen körs mot rendered screenshots (light+dark, samma VETO-mekanism som FAS 3-stängning per ADR 0046) **och** mot interaktionssökvägen / task-completion-vägen. Rendered-screenshot-granskning ensam är **inte** tillräcklig — flera av FAS 3-defekterna avslöjades först när uppgiftens genomförandeväg granskades, inte den statiska bilden.

### Beslut 3 — Källförankrad checklista

Granskningen förankrades i auktoritativa källor som Klas web-sökte 2026-05-17:

- **Boeke — "UX anti-patterns: mixing status and action":** synlig systemstatus + förankrad övergång; en kontroll ska inte visa nästa-state som om det vore nuvarande.
- **GOV.UK Design System — "one thing per page" + form-structure:** dela upp komplexa flöden; blanda aldrig två formulär visuellt.
- **GOV.UK Design System — Tag-komponent (status) + Summary card-pattern:** avgränsa områden av samma typ visuellt.
- **Norman — *The Design of Everyday Things*:** synlighet av systemstatus.
- **Krug — *Don't Make Me Think*:** användaren ska inte behöva gissa.
- **Wroblewski — *Web Form Design*:** formulär som förhindrar irreversibla misstag.

Checklistan (minst, ej uttömmande):

1. Kan en förstagångsanvändare slutföra kärnuppgiften utan att gissa?
2. Är systemstatus synlig och förankrad i nuläget (ej nästa-state visad som nuvarande)?
3. Är irreversibla handlingar tydligt märkta och konsekvens-kommunicerade **före** handlingen?
4. Blandas separata uppgifter/formulär visuellt utan avgränsning?
5. Finns sektions-/områdesavgränsning (Tag/Summary card-anda)?

### Beslut 4 — Komplementaritet oförändrad

design-reviewers komplementaritet mot code-reviewer (arkitektur/kod) och security-auditor (PII/auth) lämnades oförändrad. Flödesbegriplighet är design-reviewers domän eftersom den bor på samma rendered-yta som estetik och a11y — inte code-reviewers eller security-auditors.

## Alternativ som övervägdes

### Alt A — Utöka design-reviewer-mandatet additivt, ingen ny agent (VALT)
**För:**
- Fyller den strukturella §1-luckan utan att splittra mot en sakgräns som inte finns
- Anti-bloat-doktrin respekterad (roster-gap-CTO §1.2; ADR 0036-precedens)
- Återanvänder befintlig rendered-screenshot-infrastruktur i light+dark
- Estetik och begriplighet bedöms av samma agent som redan tittar på ytan
**Emot:**
- design-reviewers scope växer — risk att en körning blir tyngre
- Mandatet blir bredare; kräver disciplin att inte låta flödes-granskningen tränga ut tokens/a11y

### Alt B — Skapa en ny dedikerad UX-/flödesbegriplighets-agent (AVVISAT)
**För:**
- Skarp agent-gräns mellan "ser ut" och "är att använda"
**Emot:**
- Agent-bloat mot anti-bloat-doktrinen (roster-gap-CTO §1.2) — agent-gräns där ingen sakgräns finns
- Dubblerar design-reviewers screenshot-infrastruktur (light+dark)
- Estetik och begriplighet bor på samma rendered-yta — splittringen är artificiell

### Alt C — Ingen ändring; betrakta FAS 3-defekterna som en batch-miss (AVVISAT)
**För:**
- Ingen agent-fil-ändring
**Emot:**
- Rotorsaken var **strukturell**, inte en enskild körnings-miss — gate-kedjan kunde inte fånga §1-brott oavsett hur noggrant en körning gjordes
- En gate som strukturellt inte kan fånga det den påstår täcka **ljuger om sin täckning** (samma gate-ärlighets-princip som ADR 0044). Tre GO på 5 allvarliga defekter är beviset.

## Konsekvenser

### Positiva
- design-reviewer-VETO täcker nu flödesbegriplighet (task-completion), inte bara estetik/tokens/a11y — den strukturella §1-luckan är stängd.
- Ingen agent-bloat — befintlig infrastruktur och rendered-yta återanvänds (anti-bloat-doktrin upprätthållen).
- Granskningen är källförankrad (Boeke / GOV.UK / Norman / Krug / Wroblewski) — bedömningen är inte godtycklig utan har auktoritativ grund.
- Interaktionssökväg-kravet fångar defekter (irreversibilitet, sammanflätade formulär) som statisk screenshot ensam missar.
- Gate-kedjans täckning speglar nu sin egen utfästelse — ärlig gate i ADR 0044-anda.

### Negativa
- **design-reviewers scope växer** — en körning blir tyngre och risken finns att flödes-granskningen tränger ut tokens/a11y-noggrannhet. Mitigering: flödes-granskningen är ett **additivt femte område**, inte en ersättning; de fyra befintliga områdena är oförändrade i agent-filen.
- Checklistan är "minst, ej uttömmande" — en framtida defekt-klass kan ändå falla utanför de fem punkterna. Mitigering: checklistan är källförankrad och formulerad som princip (begriplighet), inte uttömmande regel-lista; ny defekt-klass triggar revidering (samma mönster som ADR 0044 ratchet-revidering vid Klas-GO).
- Mandat-utökning är en agent-fil-ändring, inte en spec-edit — risk att framtida läsare av enbart DESIGN.md inte ser flödes-mandatet. Mitigering: denna ADR + agent-fil-cross-ref + ADR-index-rad är auktoritativ källa.

## Implementation

- **ADR 0047** levererad denna session (denna fil), status **Accepted** (explicit Klas-GO 2026-05-17).
- **`.claude/agents/design-reviewer.md`** utökad additivt: nytt "Area 5: Task-completion / flödesbegriplighet" lagt till i "Review scope"-sektionen + checklistan + interaktionssökväg-kravet + källförankring; befintliga Area 1–4 (estetik / tokens / a11y / svensk copy) **oförändrade**; "Review process" Step 3 + severity-tabellen + "What design-reviewer does NOT do" + collaboration-sektionen synkade additivt.
- **design-reviewer-VETO-villkoret** (ADR 0046 Implementation: rendered-screenshot-granskning light+dark = Fas-stängnings-gate, ej push-blocker) **kvarstår oförändrat** — flödesbegriplighet ingår nu i samma VETO, inte en ny separat gate.
- **ADR-index** (`docs/decisions/README.md`) uppdaterat additivt med ADR 0047-raden (docs-keeper underhåller indexet — denna ADR levererar raden additivt; docs-keeper verifierar cross-refs vid session-end).
- **FAS 3-defekterna** (`/ansokningar/[id]` v0.2.13-dev) åtgärdas separat av nextjs-ui-engineer under design-reviewers utökade VETO — utanför denna ADR:s scope (denna ADR formaliserar gate-mandatet, ej UI-fixen).

## Referenser

- CLAUDE.md §1 (civic-utility-identitet — premissen gaten nu täcker), §8 punkt 6 (DoD: tangentbord/screenreader/begriplighet), §9.2 (agent-invocation-disciplin, anti-bloat CTO+architect-precedens)
- DESIGN.md + `.claude/skills/jobbpilot-design-*` (design-reviewer-auktoritetskällor)
- `.claude/agents/design-reviewer.md` (utökad agent-fil — Area 5)
- ADR 0044 (gate-ärlighets-/icke-regressions-mönster), ADR 0046 (FAS 3-scope + design-reviewer-VETO-villkor), ADR 0003 (design as skills), ADR 0016 (civic design language), ADR 0037/0038 (designsystem v2 / GOV.UK-läsbarhetsgolv)
- roster-gap-CTO 2026-05-17 §1.2 (anti-bloat-doktrin — ingen ny agent där ingen sakgräns finns), ADR 0036-precedens (CTO+architect bär infra utan dedikerad infra-agent)
- Boeke, "UX anti-patterns: mixing status and action" (web-sökt 2026-05-17)
- GOV.UK Design System — "one thing per page", form-structure, Tag-komponent, Summary card-pattern (web-sökt 2026-05-17)
- Norman, *The Design of Everyday Things* (synlighet av systemstatus)
- Krug, *Don't Make Me Think* (användaren ska inte behöva gissa)
- Wroblewski, *Web Form Design* (formulär som förhindrar irreversibla misstag)
- session-log 2026-05-17 FAS 3-stängning

---

*ADR-index underhålls av docs-keeper. Detta beslut formaliserar utökningen av design-reviewer-mandatet med task-completion/flödesbegriplighet efter den strukturella granskningslucka som FAS 3-stängningen avslöjade.*
