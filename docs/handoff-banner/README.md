# Handoff: JobbPilot banner "F4 Hybrid" + grönt accentsystem

Innehåll:

| Fil | Vad |
|---|---|
| `CC-PROMPT.md` | Komplett prompt till CC — kopiera allt under strecket |
| `referens/F4-banner-referens.html` | Levande spec i ren HTML/CSS. Alla tokens i `:root`, dark mode via `html.dark`. Öppna i browser, toggla light/dark nere till höger. |
| `referens/F4-light.png` | Målbild light mode |
| `referens/F4-dark.png` | Målbild dark mode |

## Beslutslogg (kort)

- Banner tillbaka på Jobb-sidan, men JobbPilot-egen — inte Platsbanken-klon
- Form: "F4 Hybrid" — inramad platta (marginal + 6px radius) + asymmetrisk komposition (display-rubrik vänster, sök höger)
- Färg: mörkgrön gradient `118deg, #0B2A1E → #14503A → #1E6B4C` (granskog — kall, ej FK-grön i ton)
- Accentbyte i hela appen: blå → grön `#15603F` (light) / `#6EE7A8` (dark). Status-färger oförändrade.
- Kontroller i bannern neutrala (vita / ink) — inga gröna knappar
- Fokus: grön ring på vitt, vit ring i bannern, ljusgrön i dark mode. Inte orange (krockar med warning).
- Stats stannar i headern (globala, alla sidor)
- Samma bannerfärg på alla sidor tills vidare; per-sida-nyanser ev. senare
- Loggan ses över separat — ingår inte i detta

Beslutat 2026-06-10 · utforskning: `Banner-utforskning v2.html` i Claude Design-projektet
