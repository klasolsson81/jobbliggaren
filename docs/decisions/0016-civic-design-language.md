# ADR 0016 — Civic design language som arkitekturkrav

**Datum:** 2026-05-06
**Status:** Accepted
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0003 (Design as skills), ADR 0015 (Frontend-stack, CSS-first tokens), DESIGN.md

---

## Kontext

JobbPilot är en civic utility — ett verktyg som hanterar jobbansökningar med den ton och trovärdighet man förväntar sig av en svensk samhällstjänst (1177, Digg, GOV.UK, Stripe Dashboard). Det är inte en SaaS-produkt som säljer sig på visuell spänning.

Under STEG 4a (frontend-bootstrap) installerades shadcn-baskomponenter (Button, Input, Card) och granskades mot civic spec. En design-review av 3 komponenter gav 7 fynd: 2 blockerande, 2 allvarliga, 3 mindre. Specifikt:

- `Card` använde `ring-1` istället för `border border-border`
- `Card` använde `rounded-xl` (>6 px, mot civic radius-regel)
- `Button` hade `rounded-lg` på basklassen (ska vara `rounded-md`)
- `Button` hade ej synligt hover-state på default-variant
- Shadcns `.dark`-preset innehöll `oklch(0.488 0.243 264.376)` — en indigo-violet kulör som bryter mot civic palettbegränsning

shadcn-komponenters defaults är designade för SaaS/produkttooling. De är inte civic-friendly ur kartongen. Utan ett explicit arkitekturkrav och ett obligatoriskt review-steg efter varje komponentinstallation kommer design-drift att uppstå gradvis och vara svår att återta.

Parallellt har ADR 0015 beslutat om CSS-first-konfiguration med OKLCH-färgmodell, och ADR 0003 om design-systemets struktur som skills. Denna ADR formaliserar det tredje fundamentet: **vad** civic design language innebär och **hur** det enforcaras.

## Beslut

**DESIGN.md är den enda auktoritativa källan för all visuell design i JobbPilot.** Token-baserad design med civic estetik styr alla UI-beslut.

Civic design language i JobbPilot definieras av:

1. **Estetisk referens:** 1177, Digg, GOV.UK, Stripe Dashboard — inte Linear, Vercel, Notion eller typisk AI-app-estetik.
2. **Teknisk grund:** OKLCH-färgmodell via Tailwind v4 CSS-first (se ADR 0015); Hanken Grotesk som typsnitt; 4px-grid för spacing; radius max 6 px i app-UI.
3. **Låst tokensystem:** Färger, typografi, spacing och radius definieras i `globals.css` via `@theme {}`-blocket och replikeras i `.claude/skills/jobbpilot-design-tokens/`. Ingen komponent eller sida introducerar tokens utanför detta system.
4. **Obligatorisk design-review:** Varje `pnpm dlx shadcn@latest add <komponent>` kräver en invokation av `design-reviewer`-agenten innan komponenten används i produktion-UI.

## Alternativ som övervägdes

### Alt A — Inga explicita restriktioner, civic-ton som konvention

**För:** Lägre overhead. Utvecklare som känner varumärket håller tonen organiskt.
**Emot:** shadcn-defaults bryter aktivt mot civic spec (bevisat i STEG 4a). Konventioner utan enforcement eroderar. Med en ensam utvecklare och AI-assisterade kodskrivare riskerar varje ny komponent att ta in oavsiktliga SaaS-estetik-element (gradient, glow, indigo-violet). Avvisat.

### Alt B — Civic design language som ADR med explicit förbjudna mönster och review-krav (valt)

**För:** Formaliserar de krav som redan existerar i DESIGN.md och CLAUDE.md §5.2. Ger tydlig reference-punkt när design-reviewer flaggar avvikelser. Gör det möjligt att hänvisa till ett enskilt dokument i PR-diskussioner.
**Emot:** Ytterligare ett ADR att underhålla. Mitigering: DESIGN.md är SSOT för token-värden; denna ADR dokumenterar *varför* och *vilka governance-krav* som gäller — inte token-värdena i sig.

### Alt C — Utöka DESIGN.md med governance-sektion

**För:** Samlad spec.
**Emot:** DESIGN.md äger filosofi och pedagogi; ADR-formatet äger beslutshistorik och enforcement-regler. Att blanda dem suddar ut gränsen mellan "spec" och "beslut". Avvisat.

## Konsekvenser

### Positiva

- **Konsistent, pålitlig UI** som matchar svenska offentliga sektorns förväntningar.
- **Token-baserat system** gör framtida omdesign till en `globals.css`-ändring, inte en komponent-genomgång.
- **Explicita förbjudna mönster** (se nedan) förhindrar design-drift vid shadcn-uppgraderingar och komponent-tillägg.
- **Review-krav per shadcn-komponent** fångar civic-avvikelser tidigt — STEG 4a visade att 10 minuter per komponent är tillräckligt.
- **Tydlig reference-punkt** i PR-diskussioner: "detta bryter mot ADR 0016, paragraf X."

### Negativa

- **shadcn-komponenter kräver post-install review** innan de är produktionsklara. Kostnad: ~10 min per komponent. Mitigering: `design-reviewer`-agenten automatiserar granskningen.
- **Mindre visuellt spännande** — avsiktligt. Civic utility ska inte vara visuellt spännande.
- **Dark mode ej implementerat i Fas 0.** Framtida dark mode kräver att civic-lämpliga neutrala mörka tokens definieras — inga kulörtonade sidofält, inga blåtonade bakgrunder. Flaggat som teknisk skuld för Fas 1–2.
- **Hanken Grotesk är en Google Font** med normal extern request. Mitigering: `next/font/google` subsets och self-hostar via build-time-optimering.

## Förbjudna mönster

Följande är **blockerade** i all JobbPilot UI-kod. En `design-reviewer`-flagg på dessa utgör blockerande fynd:

| Kategori | Förbjudet |
|---|---|
| Gradienter | `background-gradient`, `linear-gradient`, `radial-gradient`, `gradient-to-*`, Tailwind `bg-gradient-*`, animerade gradienter, shimmer med rörliga gradienter |
| Glass morphism | `backdrop-blur`, `bg-white/10 backdrop-blur`, frosted glass-effekter |
| Glow / neon | `box-shadow` med kulörta glow-värden, `drop-shadow` med kulör, neon-färger |
| Förbjuden palett | Tailwind `indigo-*`, `violet-*`, `purple-*`, `fuchsia-*`; shadcn `.dark`-preset med indigo-kulör |
| Radius | `rounded-xl`, `rounded-2xl`, `rounded-3xl`, `rounded-full` i app-UI (undantag: `rounded-full` för pill-badges/chips är tillåtet) |
| Skuggor | `shadow-lg`, `shadow-xl`, `shadow-2xl` (max tillåtet: `shadow-md`) |
| Copy | Emoji i UI-copy eller kod; utropstecken i användarriktad text |

## Implementation

**Review-krav i praktiken:**

1. Kör `pnpm dlx shadcn@latest add <komponent>`.
2. Invokera `design-reviewer` med diff mot civic spec.
3. Åtgärda blockerande och allvarliga fynd i `src/components/ui/<komponent>.tsx`.
4. Commit med fynd dokumenterade i commit-meddelande eller PR-beskrivning.
5. Komponenten får användas i produktion-UI.

**Governance-stöd:**

- `design-reviewer`-agenten (`.claude/agents/design-reviewer.md`) enforcar dessa regler på frontend-diff:ar.
- `jobbpilot-design-principles`-skill triggas automatiskt på design-diskussioner och laddar do/don't-reglerna.
- `jobbpilot-design-tokens`-skill triggas på CSS/Tailwind-ändringar och verifierar token-compliance.

## shadcn preset-val (Nova)

shadcn CLI v4.7 använder namngivna presets (Nova, Vega, Maia m.fl.) istället för äldre "new-york"/"default". JobbPilot använder **Nova** (Radix Themes-komponenter + Lucide-ikoner, kompakt spacing). Vega (klassiska shadcn-proportioner) övervägdes men förbigicks. Civic tokens i `globals.css` overridar de flesta preset-specifika stilar när JobbPilots design-system är aktivt — preset-valet är sekundärt relativt token-overriderna.

## Relation till DESIGN.md och andra ADR:er

Denna ADR **ersätter inte** DESIGN.md. DESIGN.md är spec-dokumentet (token-värden, komponentmönster, copy-riktlinjer). Denna ADR dokumenterar *varför* dessa constraints existerar, *varför* de avviker från mainstream SaaS-design, och *vilka governance-krav* som gäller.

- **ADR 0003** — beslutar om design-systems structure som skills (hur specen är organiserad)
- **ADR 0015** — beslutar om CSS-first OKLCH-konfiguration (den tekniska grunden för tokensystemet)
- **ADR 0016 (denna)** — beslutar om civic design language som arkitekturkrav (vad som gäller och varför)
