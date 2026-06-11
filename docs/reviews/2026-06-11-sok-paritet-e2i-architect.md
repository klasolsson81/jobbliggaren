# dotnet-architect — Fas E2i spegel-sökfält (arkitekturdom)

**Datum:** 2026-06-11
**Agent:** dotnet-architect (INLINE; senior-cto-advisor beslutar multi-approach efter denna)
**Scope:** Klas rendered-feedback + AskUserQuestion-val på E2h (#52, main `5f4e1cc`): "Normal ruta som speglar söket" — inga chips i fältet; ALLA taggar (även fritext-q-ord) i filter-raden med ×; ×-borttagning uppdaterar fältets text.
**Status:** Design-dom (read-only; CC har sparat verbatim).

---

## Sammanfattning

Rekommendation: **Variant C′** — fältets text är användarens redigerings-buffert (källa under egen skrivning), URL förblir persistent sanning, och bron är **envägs delta-parse** (text → state) plus en **representabilitets-gated serialize** (state → text) som körs ENDAST vid extern divergens. Varken ren A eller ren C överlever rundtripps-fallgroparna; B bryter mot Klas låsta preview och avvisas. Tre semantik-frågor flaggas till CTO/Klas (popover-text-spegling, "Rensa alla filter"-scope, Title-förslagets q-replace-semantik).

---

## M1 — text⇄state-modellen (KÄRNAN)

**Varför ren Variant A faller.** "Fältet = serialize(state), re-parse vid commit-punkter" har två dödliga egenskaper:

1. **Texten hoppar under pågående skrivning.** Serialize måste välja en kanonisk ordning (state-arrayerna bär ingen skriv-ordning). Användaren skriver "systemutvecklare göteborg" → vid delimiter-commit deriveras texten om till "Göteborg systemutvecklare" (chip-ordningen region→municipality→occupationGroup→q). Text som re-ordnas under caret är fientligt och bryter exakt det Klas godkände: texten han skrev ska stå kvar.
2. **Rundtrippen parse(serialize(s)) = s är INTE en gratis-egenskap.** Den kräver bevis per label-klass, och det finns labels där den inte kan hålla (se rundtripps-inventeringen). Ren A har ingen plan för dem.

**Varför ren Variant B avvisas.** q-only-spegling betyder att "Göteborg" försvinner ur fältet i samma ögonblick ordet taggas som ort — ordagrant det beteende Klas underkände i E2d/E2h-loopen och motsatsen till den godkända previewen. Inget arkitekturresonemang trumfar ett låst produktval.

**Varför ren Variant C nästan är rätt — och var den läcker.** C är **inte** E2g/E2h:s två-sanningar-fälla: state är en deterministisk funktion av texten — state kan aldrig bli stale *relativt texten*. Läckan är motsatt riktning: **state ändras utanför texten** (popover-val, toolbar-×, recent-search-navigation, popstate, suggestion-val av ambiguös label). Då gäller inte längre parse(text) = state, och ingen mekanism i ren C upptäcker det. "parse(text) = state"-invarianten som *likhet* är dessutom obevisbar: en popover-vald dimension finns i state men inte i texten — likheten är fel invariant från start.

**Rekommenderad modell: Variant C′ — buffert + envägs-bro + invariant-checkad resync.**

Den korrekta invarianten är en **delmängd**, inte en likhet:

> **I1: parse(fieldText) ⊆ urlState** — allt texten gör anspråk på finns i state; state får innehålla MER (popover-dimensioner, disambiguerade förslag, icke-representabla labels).

Fyra regler underhåller I1:

1. **Egen skrivning: delta-sync, aldrig replace-sync.** Vid varje commit-punkt: `Δ = parse(text_nu) − parse(text_vid_förra_commit)` (cached), applicera Δ (adds OCH removes) på urlState. Löser mid-text-redigering korrekt: raderar användaren "Göteborg" mitt i texten försvinner ortfiltret vid nästa commit-punkt — *utan* att popover-valda dimensioner (som texten aldrig gjort anspråk på) rörs. Texten är auktoritativ **för sitt eget bidrag**, inte för hela staten.
2. **× i filter-raden: kirurgisk text-edit, inte re-serialize.** Ta bort chip:ens label/ord case-insensitivt ur texten + kollapsa avgränsare; state-operationen är fortsatt `removeChipFromState` (SPOT). Kirurgisk edit bevarar användarens ord-ordning. Var taggen aldrig i texten (popover-vald): texten lämnas orörd — korrekt per I1.
3. **Extern state-ändring: resync vid base-skifte.** Utöka prev-prop-sentinel-mönstret: vid base-skifte, om I1 bryts → kirurgisk borttagning av de orden; räcker inte → full `serialize(urlState)` som sista utväg (kanonisk ordning acceptabel — händelsen var extern). Toolbar-× → text-uppdatering **utan ny cross-island-kanal** — URL:en är redan kommunikationsbussen (E2g).
4. **Serialize är representabilitets-gated** (nedan).

**Rundtripps-inventeringen.** serialize får ENDAST emittera en dimension som text om `parse` bevisligen återfinner exakt den. Ett rent predikat `isTextRepresentable(label, index)` styr allt. Klasser som faller:

- **Ambiguös label** (samma label på flera noder): parse → fritext per gissa-aldrig-regeln → dimensionen skulle tyst degradera till q-ord. Serialiseras ej.
- **Label som innehåller komma**: komma är avgränsare → labeln splittras vid parse. Serialiseras ej.
- **Label med ledande `+`/`-`-ord**: operator-strippen muterar ordet → mismatch. Serialiseras ej.
- **Cross-boundary-capture**: label A:s sista ord + label B:s första ord bildar en tredje label vid greedy-match. Mitigering: holistisk verifiering `parse(serialize(s)) ⊆ s` vid serialize; vid miss → komma-separera taggarna och verifiera om; kvarstår miss → uteslut labeln.

Icke-representabla dimensioner lever **enbart i filter-raden** (komplett sanning) — fältet är *best-effort-spegel*, raden är *total spegel*. Ska stå i komponent-doc-kommentaren.

**Greedy longest-match (multi-ord)**: ja — krävs för rundtrippen och ger Klas-visionens bonus ("Upplands Väsby" handskrivet auto-matchar; "Stockholms län" splittras inte till två q-ord). Regel: längsta **unika** n-gram vinner (bounded av taxonomins max-label-ordlängd, exporteras från `buildLabelIndex`); ambiguöst n-gram → prova kortare; ambiguitet på längd 1 → fritext (befintlig regel som basfall). n-gram spänner aldrig över komma.

**Popover-val i fältet:** under C′ är svaret **nej, popover-val skrivs INTE in i texten**. Klas-specen säger fältet "visar söktexten" — det användaren skrev — inte hela filterstaten. Filter-raden visar ALLA taggar; fältet visar det skrivna. **Flaggas till CTO/Klas som explicit semantik-bekräftelse.**

## M2 — parse-timing

- **Commit-punkter:** avgränsar-keystroke i aktiva ordet (omedelbart — Klas live-krav), Enter/Sök (finalizeAll), förslags-val. Blur-finalize införs INTE (utanför spec).
- **Mid-text-redigering: caret-segment-exkludering**, inte debounce. Generalisera `remainderSeed`-logiken till "**segmentet som innehåller caret är pågående**": `parseSearchText(text, caretIndex)` exkluderar caret-segmentet tills caret lämnar det eller avgränsare/Enter kommer. Förhindrar att radering mitt i "Göteborg" momentant släpper ortfiltret + committar q-ordet "götebor" + server-refetch per keystroke (ADR 0045-hygien). Konsekvensen — träfflistan visar gamla filtret medan ordet redigeras — är förutsägbar och rätt.
- Debounced-full-reparse avvisas: committar halvfärdiga mid-text-ord till URL (fel state, server-churn) och bryter omedelbar-vid-delimiter-kravet i kanten.
- `router.replace {scroll:false}` består (E2h VAL 2); toolbar-push-asymmetrin består.
- **q-max-guarden** flyttar in i parse: vägrade ord ingår inte i parse-output men flaggas → `limitNotice` består. Vägrade ord står kvar som text — avsiktligt.

## M3 — filter-radens q-taggar

- Toolbar flippar till `includeQ: true` — kontraktet finns redan. q-tagg-label = ordet, ingen resolver-ändring.
- **Ikon/särskiljning för q-taggar**: design-reviewer-fråga. `aria-label="Ta bort filter ${label}"` blir semantiskt fel för ett sökord — copy-detalj.
- **"Rensa alla filter" — semantik-beslut till CTO/Klas:** E2e-domen ("q bevaras") byggde på premissen att q inte var en tagg i raden — E2i river premissen. (a) nolla ALLT + töm fältet (least surprise), eller (b) behåll q men särskilj sökord från filter i label. Premiss-skiftet = nytt Klas-beslut, inte tyst E2e-arv.
- Hero-fältet reagerar på toolbar-driven q-ändring via base-skifte-resyncen — ingen ny mekanism.

## M4 — rivning

- **`chip-search-field.tsx` raderas** + `.jp-chipfield`/`.jp-filterchip--field`-blocken i globals.css. `JobbHeroSearch` renderar `JobAdTypeahead` direkt. Enradig text-input löser layoutbuggen per konstruktion.
- **`onEmptyBackspace`: bort** (Backspace redigerar text naturligt). **`inputRef`: bort** (enda konsumenten var chip-fältet; YAGNI). **`selectOnTab`: består** (Klas-spec oförändrad).
- **aria-annonserna "Lade till/Tog bort": BEHÅLLS** — viktigare nu: visuella feedbacken (taggen) sitter under träfflistan, långt från fältet.
- **Förslags-val:** (i) committas som dimension via `composeSuggestionChip` (SPOT — disambiguering kan inte återskapas ur text) OCH (ii) labeln skrivs in som text, ersätter pågående prefix-segment + trailing space, **gated av `isTextRepresentable`**. Ambiguös label vald explicit → state får den, texten får den INTE (parse skulle bryta I1) — taggen syns i filter-raden. Dokumenteras.
- **No-JS/hydration:** pre-hydration `name="q" defaultValue={q}` består. Post-hydration: synliga inputen förblir **namnlös**, initieras till `serialize(urlState)`; hidden q bär committad residual-q. Synliga texten får ALDRIG name="q" — native submit av "Göteborg systemutvecklare" som q skulle dubbel-filtrera. Hydration-text-växlingen dokumenteras.

## M5 — SPOT: kontraktsändringar

| Modul | Ändring |
|---|---|
| `tokenize.ts` | `buildLabelIndex` → ord-sekvens-index (multi-ord, exporterad max-n-gram-längd). `tokenizeDraft` → `parseSearchText(text, caretIndex?)` — ren parse (text → anspråk per axel). Nya rena: `diffParse`/`applyParseDelta`, `serializeSearchText(state, resolver, index)` + `isTextRepresentable`. `sameUrlState` består. DOM-fritt (§2.4) — **rundtripps-teoremet `parse(serialize(s)) ⊆ s` ska vara property-aktig unit-test**. `tokenize.test.ts` skrivs om in-scope. |
| `chip-models.ts` | Kontrakten oförändrade; toolbaren byter `includeQ`-flaggan. |
| `chip-composition.ts` | **Kontraktsändring, flaggas:** Title-grenen ERSÄTTER hela q idag — med q-ord-som-taggar raderar det användarens övriga sök-taggar vid Title-val. Ska bli append-med-dedupe. Egen test-uppdatering + PR-body-omnämnande. |

## Övrigt

- **TD (§9.6):** inga lyft. Minus-strippen består (Klas-pending, egen fas).
- **Testdisciplin:** delta-sync, caret-exkludering, representabilitets-gaten, rundtripps-teoremet = rena funktioner — unit-tester FÖRE komponent-wiring (§2.4). `chip-search-field`-tester raderas med komponenten.
- **CTO-beslutspunkter:** (1) popover-val speglas EJ i texten — bekräfta; (2) "Rensa alla filter"-scope (a/b) — Klas; (3) Title-förslagets append-semantik — bekräfta.

## Referenser

CLAUDE.md §2.4/§4.3/§5.2/§9.6 · ADR 0062 (websearch_to_tsquery-lexem) · ADR 0067 Beslut 5b (composeSuggestionChip-SPOT) · ADR 0045 (ingen replace-per-keystroke) · E2g/E2h-domarna (URL-sanning, prev-prop-sentinel).
