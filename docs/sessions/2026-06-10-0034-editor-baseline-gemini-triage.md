---
session: editor-baseline-gemini-triage
datum: 2026-06-10
slug: editor-baseline-gemini-triage
status: levererad (PR pending merge; spec-edit-rad pending Klas-approve)
commits: se PR chore/editor-baseline
---

# Session 2026-06-10 — Extern idé-triage (Gemini) + editor-baseline

## Mål

Klas klistrade in en Gemini-genererad prompt ("Integrera Graphify som
kontext-motor + optimera VS Code-miljö") med explicit direktiv: exekvera INTE
rakt av — gör egen discovery + webbsökning, triagera per idé, komplettera med
egna fynd.

## Triage-utfall (rapporterad i chatten, Klas-GO på åtgärdslistan)

| Gemini-idé | Dom | Skäl |
|---|---|---|
| Installera Graphify (`pip install graphify`) | AVVISAD nu; pilotbar efter MVP | Verktyget är äkta (graphifyy på PyPI, v0.8.36, C#-stöd via tree-sitter) men Geminis kommando pekar på FEL paket (`graphify` = orelaterat; typosquat-terräng med `graphifyyy`); 0.x-mognad; värdet otestat mot befintlig Explore-subagent/docs-infra |
| CLAUDE.md "Graph-augmented workflow" + anti-overhead-regel | AVVISAD | Direkt konflikt med §9.4 (rå output-krav, intjänad ur omformulerings-glidning) + §1.5 session-protokoll; CLAUDE.md är spec-fil |
| JSON state-block i current-work.md | Rätt diagnos, fel medicin | Filen var 866 rader varav ~700 historik → split till arkivfil (tech-debt-archive-precedensen), INTE maskinläsbart block |
| Rensa session-start-templaten | AVVISAD som princip; ETT äkta fynd | Sektionerna har incident-härkomst; men rad 31–33 hade döda AWS-förkrav (ADR 0066-drift) → städade |
| Extension-triage (Error Lens, GitLens, Cline) | DELVIS | Error Lens + GitLens IN; Cline AVVISAD (autonom AI-agent = parallell-agent-risk + extra LLM-datakanal; konkurrent till sanktionerad Claude Code-kanal) |

**Egna fynd utöver Gemini:** CLAUDE.md §11.2-drift (.editorconfig + .vscode/*
lovade men obefintliga — sessionens huvudleverans); §11.3 nämner `make dev`/
`pnpm dev:up` som inte heller finns (EJ åtgärdat — kräver eget beslut om
skapa-vs-spec-justera); stale AWS-rader i session-start-templaten.

## CTO-dom (senior-cto-advisor `a40d8b4eb06b197fd`, rapport inline i PR-body)

- **(a) extensions.json:** CC-listan + `ms-azuretools.vscode-containers`
  (ID web-verifierat per §9.5); code-spell-checker AVVISAD (tvåspråkig kodbas
  = false-positive-densitet); `unwantedRecommendations` för Cline + Copilot.
- **(b) settings.json:** Variant B — spegla pre-commit-gates on-save (DRY,
  Hunt/Thomas); Variant C avvisad (YAGNI; files.exclude maskar fil-state mot
  §9.4-disciplinen).
- **(c) .editorconfig:** Variant B — warning-severity ENDAST §3-spårbara
  regler ("EnforceCodeStyleInBuild utan regler är teater"); tre villkor:
  §3-mappning i PR-body, grön build + format-verify innan commit, demote till
  suggestion vid icke-trivial röd.
- **(d) current-work-split:** Variant B (arkivfil, tech-debt-archive-
  precedensen); AWS-rensning i templaten godkänd. **CLAUDE.md §1.6-rad =
  spec-edit → Klas kör approve-spec-edit.sh.**

## Genomförande — avvikelser och detours

1. **EF-migrations-smäll:** första bygget med .editorconfig gav 34× IDE0161
   (EF-scaffold = block-scoped namespace). Löst med
   `[src/JobbPilot.Infrastructure/**/Migrations/*.cs]` `generated_code = true`
   (kanonisk mekanism, ej massredigering av genererad kod). Identity/Migrations
   krävde generaliserat mönster (första försöket täckte bara Persistence).
2. **Imports-ordering:** `dotnet_sort_system_directives_first` avtäckte 11
   filer — fixade mekaniskt via `dotnet format` (CTO-villkor 3). Diff
   verifierad ren omsortering.
3. **Async-suffix-naming-regeln MEDVETET utelämnad:** xUnit-testnamn
   (`<Class>_<Scenario>_<Expected>`) är async utan Async-suffix — regeln hade
   exploderat i testprojekten. Dokumenterat här + PR-body.
4. **Naming-regler kalibrerade empiriskt:** `private const` är PascalCase i
   kodbasen (undantagsregel före `_camelCase`-regeln); `private static
   readonly` är PascalCase med EN `_`-outlier → suggestion, ej warning.
5. **Stack-kill ×2:** Api/Worker låste DLL:er vid full rebuild + test.
   Stoppade (PID 7904/1868), byggde grönt; stacken dök upp igen (PID
   20288/27696 — C2-CC:ns session-end-omstart) → stoppade igen för
   testsviten. **Stack omstartad + verifierad innan sessionsslut** (memory-
   rutinen).

## Beslut med bäring framåt

- Graphify-pilot: deferrad till efter MVP / vid trigger. INGEN TD lyft
  (verktygs-experiment, ej skuld — §9.6-pressad).
- CLAUDE.md §11.3 `make dev`/`pnpm dev:up`-drift: kvarstår oadresserad,
  lyfts vid nästa spec-touch (kräver Klas-beslut skapa-vs-stryk).
- Imports-ordering är nu CI-relevant: framtida PRs med osorterade usings
  faller på format-verify.

## Nästa session

Klas väljer: Fas D1 (facet-counts + typeahead-suggest, NBomber-gate) eller
Fas E (FE-picker). Startprompt genereras vid GO.
