# Design-review: Fas E2g — state-synk + recent-search-labels (`fix/sok-paritet-e2g-state-sync-recent-labels`)

**Status:** Approved
**Granskat:** 2026-06-11
**Auktoritet:** jobbpilot-design-copy, DESIGN.md, CLAUDE.md §10.3
**Diff:** main...HEAD (7533328, 7bd0738)
**FAS-DEFERRAL-MANIFEST:** rendered-verifiering deferred till Klas post-merge på Vercel; E2d-Minors/spec-edits/de-grönings-rester spårade sedan tidigare.

### Fråga 1: "+N till" — godkänd, "till" är rätt val

"N till" efter ett antal är den naturliga svenska räkneformen ("och tre till"); "+11 andra" haltar grammatiskt med plustecknet ("andra" vill ha "och" framför sig); "fler" mindre idiomatiskt i chip-kontext. Klas formulering var "eller något i den stilen" — mekaniken delegerad; "till" uppfyller copy-skillen (direkt, konkret, ingen jargong, inga utropstecken). Ingen mönsterkollision med befintliga "+N"-användningar. **Minor (FYI):** taxonomilabels med kommatecken ("Drifttekniker, IT +11 till") kan vid första anblick läsas som lista — plustecknet bryter läsningen; om Klas upplever otydlighet live är "och 11 till" en en-rads-ändring.

### Fråga 2: Hel-områdes-kollapsens ärlighet — godkänd

"Data/IT" visas ENDAST vid exakt mängd-likhet mot trädet — labeln är en sann spegling av valet (att välja samtliga grupper ÄR att välja området). Ärlighets-kanterna täckta och testade: blandfall → "+N till" på grupper (påstår aldrig "helt område" när det inte stämmer, blandar aldrig enheter); taxonomi-drift → graceful fallback (vid osäkerhet: säg mindre, ljug aldrig); deterministisk "{första}" ger igenkänning över tid.

### Fråga 3: Sync-fixens UX — godkänd, flash-risken acceptabel

Overlayt ligger kvar under hela transitionen — långsam navigering ger INTE flash mitt i flödet. Återfall till props sker bara vid avbruten navigering eller extern ändring som vinner racet — i båda fallen är props-sanningen det civic-utility-korrekta (status förankrad i faktiskt tillstånd; lögn-kopian VAR buggen). Testet stänger ett genuint status/action-mismatch-fel. **Minor (FYI):** `isPending` slängs — ingen pending-affordance på ön (ingen regression; naturligt nästa steg om Klas upplever fönstret störande live).

### Bra gjort
Anti-AI-trope-direktivet hålls (ingen pill/emoji/utrop); fallback-kedjan intakt; test-täckning på alla fyra label-regler + degradering + extern-synk.

### Sammanfattning
**0 blockers, 0 major, 2 minors (FYI).** Inget veto. Mergeklar ur design-synpunkt.
