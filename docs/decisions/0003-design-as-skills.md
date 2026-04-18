# ADR 0003 — Design-system som Claude Code-skills

**Datum:** 2026-04-18
**Status:** Accepted
**Reviderad:** 2026-04-18 (§ Beslut — DESIGN.md-strategi uppdaterad till Alt B)

---

## Kontext

JobbPilots design-system dokumenteras i DESIGN.md — en 631-rads monolitisk fil med 13 sektioner (filosofi, färger, typografi, spacing, komponenter, ikoner, kritiska mönster, copy-riktlinjer, tillgänglighet, motion, logotyp, token-mappning, review-checklista).

I Claude Code-setup-arbetet (se [SESSION-2-PLAN.md §2](../research/SESSION-2-PLAN.md)) visade research (se [CLAUDE-DESIGN-FINDINGS.md](../research/CLAUDE-DESIGN-FINDINGS.md)) att:

1. **Monolitisk design-dokumentation är ineffektivt i Claude Code.** Hela DESIGN.md laddas i varje session där frontend nämns, även om ändringen bara rör ett ikon-val. Context-kostnad är onödig.
2. **Community-konventionen är skill-baserade design-system.** Tre oberoende källor (Katherine Yeh på Medium/Bootcamp, Julian Oczkowski, Marie Claire Dean) har publicerat skill-baserade design-strukturer för Claude Code under Q1 2026. Skill-pattern gör att bara relevant delmängd laddas baserat på trigger-beskrivning.
3. **Anthropic själva** ([platform.claude.com — skills best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)) rekommenderar progressive disclosure: SKILL.md lättvikt + `references/*.md` för djupa specs.
4. **Klas förslag** i session 2.5-prompten (principles / tokens / components / copy / a11y) är inte top-level-standarden i communityn men passar JobbPilots specifika behov (låst design-system, civic-utility, liten komponentyta).

## Beslut

Splitta DESIGN.md från monolitisk spec-fil till **5 skill-mappar** under `.claude/skills/`:

```
.claude/skills/
├── jobbpilot-design-principles/    # civic-filosofi + do/don't
├── jobbpilot-design-tokens/        # färger, typografi, spacing
├── jobbpilot-design-components/    # Button, Card, Input, Table
├── jobbpilot-design-copy/          # svensk ton, microcopy
└── jobbpilot-design-a11y/          # WCAG, fokusring, keyboard
```

Varje skill-mapp följer progressive-disclosure-pattern:

```
<skill-name>/
├── SKILL.md                 # lättvikt: trigger-description + kärnregler
└── references/              # djupa specs, laddas on-demand
    └── <area>-full.md
```

**DESIGN.md** i repo-roten behåller en **dubbel roll**:
- Behåller §1 (filosofi) samt kortade sammanfattningar per §2–§10 som
  människoingång och pedagogisk inramning
- Länkar till varje skill-mapp för fullständiga specs
- DESIGN.md äger **filosofi och pedagogik**; skills äger **detaljerad spec**

Den textuella överlappningen mellan DESIGN.md-sammanfattningarna och skills
SKILL.md är medveten kuratering — inte duplicering som skapar drift-risk.
Skills äger den detaljerade specen; DESIGN.md äger den pedagogiska
inramningen. `docs-keeper` verifierar konsistens vid drift-check.

*(Revision 2026-04-18: ursprunglig text sa "ren index-fil" och "ingen
duplicering" — detta ersattes med Alt B-beslutet efter att
jobbpilot-design-principles/SKILL.md pilotades och visade att filosofi-
sammanfattningar i DESIGN.md ger värde som inte skapar underhållsproblem.)*

En ny agent, **`design-reviewer`** ([SESSION-2-PLAN §1.3.9](../research/SESSION-2-PLAN.md)), verifierar design-compliance på frontend-diff:ar parallellt med `code-reviewer`. Modell: `claude-sonnet-4-6`.

Claude Design-produkten (Anthropic Labs, lanserad 2026-04-17) används INTE för v1 produkt-UI — civic-utility-estetik passar inte default-output. Omvärderas efter klass-launch för marknadsmaterial (se SESSION-2-PLAN §18).

Figma MCP — **skip**, JobbPilot använder inte Figma.

## Alternativ som övervägdes

### Alt A — Behåll DESIGN.md som monolit

**För:** Etablerat, redan skrivet, ingen migreringstid.
**Emot:** Context-kostnad i varje FE-session. 631 rader laddas även för att ändra en placeholder-text. Mot community-trenden.

### Alt B — Använd Claude Design-produkten direkt

**För:** Skulle automatiskt generera komponenter + tokens från befintlig codebase.
**Emot:**
- Produkten är byggd för "rapid visual exploration" — motsats till civic-utility-brief.
- Handoff-bundle-format är **inte publikt dokumenterat**; inga garantier för format-stabilitet.
- Claude Design är opt-in per enterprise-org; inkluderas i Pro/Max/Team men kräver extra usage ovanpå.
- JobbPilots tokens är redan låsta; Claude Design skulle regenerera det vi redan har.

### Alt C — Figma + Figma MCP

**För:** Standard-designtooling; officiell Claude Code-integration finns (beta).
**Emot:**
- JobbPilot har ingen designer. Klas är utvecklare.
- DESIGN.md är SSOT; byta till Figma vore regression.
- MCP-server är beta; inte etablerat långsiktigt.

### Alt D — Split per process-fas (Oczkowski)

**För:** Dominant community-dimension för designer-skills.
**Emot:** JobbPilot har låst design-system och ingen process-drivna skills (discovery, IA, etc.) — split per fas skulle skapa tomma skills.

## Konsekvenser

### Positiva

- **Context-effektivitet.** Claude laddar bara `jobbpilot-design-tokens` när CSS ändras, inte hela DESIGN.md.
- **Selektiv triggering.** Olika skills triggas av olika kontexter — copy-ändring laddar `jobbpilot-design-copy`, inte tokens.
- **Progressive disclosure.** `references/*.md` laddas bara vid djupdykning, inte default.
- **Community-alignment.** Följer Anthropics officiella skill-pattern + Yeh/Oczkowski/Deans konvention.
- **Enklare underhåll.** När en token ändras, ändras en skill-fil — inte en 631-rads monolit.
- **Explicit design-review-agent.** `design-reviewer` separerad från `code-reviewer` ger tydlig ansvarsfördelning.

### Negativa

- **Spridd information.** 5 filer + 5 reference-filer vs en enda DESIGN.md. Människor som letar "allt om design" måste följa index.
- **Drift-risk.** Om tokens ändras i en skill men inte motsvarande reference-fil, uppstår inkonsistens. Mitigering: `design-reviewer` kontrollerar att tokens-användning matchar skills, och `docs-keeper`-agenten scannar skill-drift i session-end.
- **DESIGN.md-index måste hållas levande.** Om en skill läggs till/tas bort måste DESIGN.md uppdateras. Mitigering: `docs-keeper` checkar att alla skills är listade i index vid session-end.
- **Engångs-migreringskostnad.** Session 3 steg 7–8 måste faktiskt splitta DESIGN.md. Estimerat 2–3 timmar.
- **Nya beroenden i skill-triggering-logik.** 5 skills innebär 5 `description:`-fält att kalibrera. Undertriggering är risk (Anthropic rekommenderar "pushy" descriptions).

## Implementation

Sker i session 3 steg 7 (rules) + steg 8 (patterns) enligt [SESSION-2-PLAN §15](../research/SESSION-2-PLAN.md). Konkret ordning:

1. Skapa `.claude/skills/jobbpilot-design-*/`-mappar.
2. Skriv SKILL.md per mapp (trigger-description + kärnregler).
3. Skriv `references/<area>-full.md` per mapp (extraherad från DESIGN.md).
4. Skriv om DESIGN.md till index-form (§1 behålls, §2–13 länkas ut).
5. Skriv `design-reviewer`-agenten (steg 10 i session 3).

## Referenser

- [CLAUDE-DESIGN-FINDINGS.md](../research/CLAUDE-DESIGN-FINDINGS.md) — forskningsunderlag
- [SESSION-2-PLAN.md §1, §2, §18](../research/SESSION-2-PLAN.md) — konkret planändring
- [Katherine Yeh — "A Designer's Guide to Organizing AI Skills and Tools in Claude Code"](https://medium.com/design-bootcamp/a-designers-guide-to-organizing-ai-skills-and-tools-in-claude-code-f87477c35b82)
- [Anthropic — Agent Skills best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- [Anthropic — Claude Design launch](https://www.anthropic.com/news/claude-design-anthropic-labs)
