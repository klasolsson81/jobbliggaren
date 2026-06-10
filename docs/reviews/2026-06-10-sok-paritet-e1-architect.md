# dotnet-architect — Platsbanken sök-paritet Fas E1 (varm-canvas-token-arkitektur)

**Datum:** 2026-06-10
**Agent:** dotnet-architect (CSS-token-arkitektur, ej kod-skrivning)
**Scope:** Scope för varm papperston `#FAF9F6` på /jobb-hero (riktning A "Papperskontoret")

---

| Fråga | Dom |
|---|---|
| 1. Scope | **/jobb-scoped.** Rör INTE app-wide `--jp-canvas`. |
| 2. Token + mekanism | Ny `--jp-hero-canvas` i `:root`; scopa via `.jp-hero`-blockets background-källa; behåll klassnamnet `.jp-hero`. |
| 3. Dark | `--jp-hero-canvas` ärver `--jp-canvas` (`#0B1525`) i dark — ingen ny varm-dark-hex. |
| 4. Radius | 12px ut, 6px in (CTO bekräftade: cleanup, ej amendment). |

**Fråga 1 (/jobb-scoped):** `--jp-canvas` är app-wide body-baslager (globals.css rad 279), konsumeras av samtliga (app)-sidor. Att byta dess värde re-färgar /oversikt, /ansokningar, /cv, /installningar — utanför E1-scope. SRP (Martin 2017 kap. 7): en token med en konsument-axel ska inte bära ett andra ansvar. Ny scopad token = OCP-korrekt (lägg till, ändra inte den delade abstraktionen). Begränsar blast-radius (ADR 0052 §Negativa).

**Fråga 2 (mekanism):** Ny `--jp-hero-canvas: #FAF9F6` bredvid `--jp-hero-*`-blocket (rad 67–73). `.jp-hero { background: var(--jp-hero-canvas); }` istället för `var(--jp-hero-bg)`. `.jp-hero` är edge-to-edge-zonen på /jobb (V3_NATIVE_ROUTES opt-out) och renderas bara där → ingen läcka. `.jp-pagehero` (inre sidor) pekar fortsatt på `--jp-hero-bg` navy, orört. Döp INTE om `.jp-hero` (Ubiquitous Language, Evans; noll vinst, JSX-omskrivnings-risk). Innre selektorer (`__title`/`__lede`/`__searchbtn`) flippar från `#fff`-text till `--jp-ink-1`/navy-på-varm. **Inga nya hårdkodade hex** — varm-tonen via token; befintliga `#fff`/`rgba(255,255,255,…)` i hero-blocket måste till `--jp-*`-tokens (annars regel-1-brott checklista punkt 1).

**Fråga 3 (dark):** `#FAF9F6` är light-only papperston. I dark är "papper" den mörka navy-grå canvasen. `[data-theme="dark"] { --jp-hero-canvas: var(--jp-canvas); }` (#0B1525) → hero smälter in utan tvåtonad söm. Speglar hur `--jp-surface`/`--jp-canvas` redan hanteras. Ingen ny varm-dark-hex (stram palett, regel 5).

**Fråga 4 (radius):** ADR 0052 Beslut 4-dispensen "12px endast hero" var motiverad av navy-platte-eran. När hero blir content-first på varm canvas (GOV.UK/1177) finns ingen platta → 6px-golvet gäller. Flaggades som möjlig ADR 0052-mekanik-ändring → CTO-triage (utfall: cleanup, ingen amendment).

## Referenser

ADR 0052 Beslut 3 (verbatim-port, byt token-källa ej struktur)/Beslut 4 (radius-golv)/§Negativa; globals.css rad 37/67–73/279/838–982/988; design-principles regel 1/5; CLAUDE.md §9.6; memory `feedback_adr_mechanism_vs_env_phase_triage`.
