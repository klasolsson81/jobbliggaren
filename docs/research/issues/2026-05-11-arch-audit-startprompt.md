# Startprompt — Fas 1 Discovery: retrospektiv arkitekturell audit

**Skapad:** 2026-05-11 ~16:00 av föregående CC (Väg E TDs-cleanup) efter
Klas-val Två-fas-approach.
**Användning:** Klipp in hela kodblocket nedan i ny `/clear`-CC.
**Status:** Aktiv tills Fas 1-discovery levererad.

---

## Bakgrund

Klas startade senior-cto-advisor-rollen formellt först i Fas 1-stängningen
(CLAUDE.md §9.6 etablerades 2026-05-11). STEG 1-11 (tidiga Fas 0 + tidiga
Fas 1) + STEG 13a-14c (infra-stack) saknar därför CTO-decision-maker-validering,
även om dotnet-architect / code-reviewer / security-auditor körts genom
historien och arch-tester låser Clean Arch-gränserna automatiskt.

**Klas-val (2026-05-11 ~15:45):** Två-fas-approach.
- **Fas 1** = denna session-prompts uppdrag = lätt discovery (1-2h CC-tid)
  som producerar risk-rapport
- **Fas 2** = targeted deep-dive baserat på rapporten, Klas-styrd scope
  (0-8h beroende på fynd)

Avvisade alternativ: full uttömmande audit (5-10h, scope-creep-risk),
risk-områden-targeted utan discovery (hoppar potentiella blind spots),
pre-CTO-targeted endast STEG 1-11 (missar arch-val som senare STEG byggt
ovanpå).

---

## Startprompt (copy-paste-klar)

```
=== STARTPROMPT — Fas 1 Discovery: retrospektiv arkitekturell audit ===

**Skapad:** 2026-05-11 ~16:00 av föregående CC efter Klas-val Två-fas-approach.
**Förväntat HEAD:** `6e5e6b0`

**Verifiera:** `git fetch origin main && git log --oneline -8` ska visa:
  6e5e6b0 docs: Väg E TDs-cleanup session-end — TD-40 + TD-49 stängda + session-logg
  954fe1e docs: TD-49 stängd som redan-implementerad pre-TD-skapande
  6b8f087 test(web): TD-40 — leaf-path regression-bevakning för resume-schemas refines
  7ee9948 docs: Väg B a11y-pass session-end — TDs stängda + steg-tracker + session-logg
  1b0b9ec fix(web): TD-42 — in-block-fixar från design-review + TD-57 lyft
  f2b179a feat(web): TD-42 — touch-target-uppgradering till skill-doc-defaults
  52f3b45 fix(web): TD-54 — in-block-fix N1 + review-rapporter
  8cfbde4 fix(web): TD-54 — text-text-tertiary kontrast-fix för funktionell text

`git status` ska vara clean.

---

## Mandatory reads vid session-start

1. **CLAUDE.md** — hela filen, men särskilt §1.5 (session protocol), §2 (kärnprinciper), §5 (anti-patterns), §9.2 (agent-invocation), §9.6 (4h-regel + CTO-disciplin)
2. **BUILD.md** — §1 (arkitektur-översikt), §3.1 (tillåtna dependencies), §18 (fas-uppdelning)
3. **docs/steg-tracker.md** (v1.17) — full STEG-historik + alla "Lärdomar"-sektioner
4. **docs/current-work.md** — senaste session-state
5. **docs/decisions/README.md** — ADR-index (28+ ADRs)

## Uppdrag — Fas 1 Discovery (1-2h CC-tid)

Klas startade senior-cto-advisor-rollen formellt först i Fas 1-stängningen
(CLAUDE.md §9.6 etablerades 2026-05-11). STEG 1-11 (tidiga Fas 0 + tidiga
Fas 1) + STEG 13a-14c (infra-stack) saknar därför CTO-decision-maker-validering,
även om dotnet-architect / code-reviewer / security-auditor körts genom
historien och arch-tester låser Clean Arch-gränserna automatiskt.

**Hypotes:** Det kan finnas SOLID/DRY/SoC-brott eller arch-shortcuts i kod
som inte triggade dotnet-architect-trigger (under 5-fil-tröskel) eller
som passerades genom som "rimliga genvägar" under implementation-press.

**Klas-val:** Två-fas. **Denna session = Fas 1 (discovery only).** Producera
en risk-rapport. Klas reviewar. Klas bestämmer Fas 2-djup.

### Steg-för-steg

1. **Inläsning:** Läs mandatory-list ovan. Läs sedan ALL session-loggar i
   `docs/sessions/` kronologiskt — de innehåller "Lärdomar"-sektioner som
   ofta avslöjar shortcuts eller tekniska tradeoffs.

2. **ADR-genomgång:** Läs alla ADRs i `docs/decisions/0001-` till
   `docs/decisions/0028-*`. Notera särskilt ADRs som accepterar deferrals
   eller medvetna tekniska skulder (ADR 0024 D-serien, ADR 0025, ADR 0026).

3. **Invokera dotnet-architect** med detta uppdrag (kopiera in klartext):

   > **Audit-uppdrag:** Retrospektiv arkitekturell audit över JobbPilot
   > STEG 1-14 (alla commits fram till HEAD `6e5e6b0`). Fokus:
   >
   > - **Clean Architecture:** Domain-isolering (importerar Domain något
   >   utöver baseklasser?), Application↔Infrastructure-gränsen
   >   (Repository-pattern över EF Core? AutoMapper över gränsen?),
   >   Api-/Worker-kompositionsdisciplin
   > - **DDD:** Aggregat-invariant-skydd (public setters utanför EF-need?),
   >   strongly-typed IDs mellan aggregat, domain events vs handler-mutationer
   > - **CQRS:** Pipeline-behavior-ordning (Logging→Validation→Auth→UnitOfWork),
   >   handlers som gör en sak, command vs query-blandning
   > - **SOLID:** SRP-brott (fete handlers, multi-purpose services), OCP via
   >   marker-interfaces, ISP (för stora interfaces), DI-direction
   > - **DRY:** Duplikerade pipeline-konfigurationer mellan Api/Worker,
   >   options-bindning, validering-mönster
   > - **SoC:** ApplicationDbContext-injektion i fel lager, cross-cutting
   >   concerns (audit, IP-anonymisering) som läckt över lagergränser
   >
   > **Metod:** Discovery-rapport-format (CLAUDE.md §9.4). INGA kod-ändringar.
   > Spot-checks på kod via Read/Grep efter att session-loggar lästs.
   >
   > **Output-format:** Markdown-rapport till
   > `docs/reviews/2026-05-11-arch-audit-discovery.md` med strukturen:
   >
   > 1. **Sammanfattning** — top-level verdict (clean / minor-issues /
   >    significant-issues) + antal hot spots
   > 2. **STEG-för-STEG-klassning:** röd / gul / grön per STEG 1-14 med
   >    en-rads-motivering (rött = misstänkt arch-brott, gult = potentiell
   >    risk värd second-look, grönt = ingen oro)
   > 3. **Hot spot-lista** — namngivna potentiella problem-områden med
   >    fil-referenser, klassificering (Blocker/Major/Minor/Nit) och
   >    rekommenderad åtgärds-scope (in-block-fix / TD / ny refactor-STEG)
   > 4. **Strukturella spärrar som FUNGERAT genom historien** — vad
   >    arch-tester + agent-reviews + ADR-disciplin har fångat (motvikt
   >    mot "allt är problem"-bias)
   > 5. **Rekommendation för Fas 2** — vilka hot spots motiverar deep-dive,
   >    vilka kan deferas som TD, vilka kan accepteras som "rimligt val
   >    givet konstraint X"
   >
   > **Tids-budget:** 1-2h CC-tid. Inte uttömmande line-by-line — strategisk
   > scanning med spot-checks.

4. **(Optional) Invokera code-reviewer parallellt** på 3-5 högst risk-områden
   som dotnet-architect identifierar — bara om Klas-tid tillåter och Fas 1
   inte överskrider 2h.

5. **Sammanställning:** Verifiera dotnet-architect-rapporten är på disk.
   Bifoga short executive summary i denna session-rapport till Klas.

6. **STOPP:** Vänta Klas-review av rapporten. INTE påbörja Fas 2 utan
   explicit GO. Fas 2-scope avgörs av risk-rapportens fynd.

## Deliverables denna session

- ✓ `docs/reviews/2026-05-11-arch-audit-discovery.md` (dotnet-architect-rapport)
- ✓ Eventuella supplementary reviews från code-reviewer (om scope tillåter)
- ✓ Uppdatering av `docs/current-work.md` med risk-rapport-summary +
  Fas 2-rekommendation
- ✓ Session-logg `docs/sessions/2026-05-11-XXXX-arch-audit-discovery.md`
- ✓ Docs-commit + push

## Förbud

- **INGA kod-ändringar** denna session. Audit är ren observationspass.
- **INGA TDs** lyfts denna session — hot spots klassas i rapporten, Klas
  bestämmer TD-skapande vs in-block-fix vs ny refactor-STEG i Fas 2-planen.
- **INTE påbörja Fas 2** utan explicit Klas-GO efter rapport-review.
- Ändra inte BUILD.md / CLAUDE.md / DESIGN.md.

## Workflow-disciplin (per CLAUDE.md §9.2 + §9.6)

1. Discovery först — alltid (denna session ÄR discovery)
2. dotnet-architect är PRIMÄR agent. CTO-invocation hoppas över eftersom
   rapporten är inputs till Klas-beslut, inte multi-approach-val.
3. STOPP-rapport till Klas efter Fas 1, INNAN Fas 2-implementation
4. Commit + push av docs efter Klas-läs av rapport

## Första action (för dig CC)

1. Verifiera HEAD = `6e5e6b0` + clean tree
2. Läs mandatory-list
3. Läs alla session-loggar kronologiskt
4. Invokera dotnet-architect med audit-uppdraget ovan
5. Sammanställ rapport + STOPP

=== SLUT STARTPROMPT ===
```
