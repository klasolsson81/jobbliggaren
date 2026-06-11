# Code-review: Fas E2g — hero-ö-state-synk (useOptimistic) + DeriveLabel hel-områdes-kollaps

**Status:** ✓ Approved (0 Blockers, 0 Major — 4 Minor + 1 FYI) → **Minor 1–4 åtgärdade in-block (se Åtgärds-trail)**
**Granskat:** 2026-06-11 · 2 commits (7533328 FE, 7bd0738 BE)
**Auktoritet:** CLAUDE.md §2.1/§2.3/§2.4/§3.5/§3.6/§4/§5/§7; CTO-dom `docs/reviews/2026-06-11-sok-paritet-e2g-cto.md`

### Granskningspunkt 1 — useOptimistic-refaktorn: GODKÄND

CTO-krav 1 uppfyllt (`setOptimisticSelection` + `router.push` i SAMMA startTransition). CTO-krav 2 verifierat mot hela filen — ALLA läsare migrerade (ortCount, pill-badges, facetFilter, popover-selected, samtliga handlers, push-argumenten = optimistiska värdet); enda kvarvarande useState är openPop (UI-lokal, korrekt). `ort`-aliaset typsäkert (strukturell assignerbarhet; ort-selection-funktionerna bygger nya objekt — defensiv dubbel spärr i commitOrt). useMemo-dependencies: ny referens per RSC-render = själva synken (önskat); stabil mellan client-renders; useOptimistic kräver inte referensstabil bas. **Bugg-testet täcker felklassen** (gammal kod hade fallerat på rerender-asserten) + Klas faktiska symptom (0 ikryssade efter extern rensning). Befintliga tester överlever jsdom-transitionens omedelbara fallback — förklarar grön svit utan justeringar.

### Granskningspunkt 2 — DeriveLabel: GODKÄND

Tree EN gång per Handle, utanför loopen; gaten konsistent med kollaps-villkoret. Mängd-likhet korrekt (selected är VO-normaliserad distinct+sorted; Count== + All(Contains) ⇔ mängdlikhet; grupper tillhör exakt ett fält → FirstOrDefault säkert; Ordinal korrekt). Determinism verifierad hela vägen (ResolveLabelsAsync bevarar input-ordning 1:1, aldrig färre labels än ids → "+N" konsistent). CA/perf trivialt vid cap=20. CancellationToken propagerat; Clean Arch intakt.

### Granskningspunkt 3–4 — tester + kontrakt

5 nya backend-fall + FE-buggtestet täcker kärnan; inga kontrakts-regressioner (DTO/props oförändrade; kontraktstesterna orörda gröna).

### Minor

1. **Stale klass-XML-doc** — `<para>`-stycket beskrev gamla First-regeln.
2. **Tree-gatens kommentar överlovade precision** ("enda fallet kollapsen kan slå in" — q-rader når aldrig grupp-grenen).
3. **Null-tree-testet asserterade kontrakts-omöjligt tillstånd** (`null!` mot non-nullable ValueTask-kontrakt) — degraderingsfallet bör stubba tomt fält-set i stället.
4. **Test-luckor:** region-+N otestad; tree-fetch-gaten obevakad (`Received(1)` / `DidNotReceive`). FE-optimistisk-feedback svårtestad i jsdom — E2E/visual bär den (acceptabel, dokumenterad).

### FYI

CTO:s "fält→grupp-set-lookup en gång" implementerades som per-rad-skan över ~21 fält i stället för precomputed lookup — intentionen (ETT tree-fetch, ingen extern hop per rad) uppfylld; Dictionary vore onödig komplexitet vid cap=20. Ingen åtgärd.

### Bra gjort

`commit()`-konsolideringen tar bort DRY-felklassen (dubbel sanning), inte lappar den; commitOrt:s explicita konstruktion; WithMoreSuffix-enhetskommentaren; hel-områdes-testet med oordnad input verifierar VO-normaliseringen + regeln samtidigt; självbärande review-trail i kommentarer; noll yt-spridning.

### Sammanfattning
0 Blockers, 0 Major, 4 Minor, 1 FYI. **Mergeklar.**

---

## Åtgärds-trail (huvud-CC, 2026-06-11 — in-block samma PR)

| # | Fynd | Åtgärd |
|---|---|---|
| m1 | Stale klass-doc | `<para>` uppdaterad till E2g-regeln. |
| m2 | Överlovande gat-kommentar | Mjukad ("kan behövas"; q-radernas gratis-extra noterad). |
| m3 | null!-test | Omskrivet till tomt-fält-set-degradering (StubTree()). |
| m4 | Region-+N + gat-bevakning | +2 tester (region-+N; Received(1)/DidNotReceive på GetTreeAsync). 19/19 gröna. |
