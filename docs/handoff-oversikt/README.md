# Översikt — handoff-bundle

Innehåll i den här zip-filen:

```
HANDOVER-oversikt.md   ← Primär spec — börja här
HANDOVER-v3.md         ← Övergripande v3-designsystem (referens)
jobbpilot-v3.css       ← Komplett CSS — sök "Översikt — civic dossier"
screenshots/           ← 5 målbilder (light + dark)
  01-oversikt-top.png
  02-oversikt-notiser-information.png
  03-oversikt-sammanfattning.png
  04-oversikt-dark-top.png
  05-oversikt-dark-sammanfattning.png
src/
  oversikt.jsx         ← Sidans React-komponenter (huvudkälla)
  app.jsx              ← Router/mount
  shell.jsx            ← Header med Översikt som första nav-item
  data.jsx             ← Mock-data (pipeline, CVs, sökningar)
  icons.jsx            ← Lucide-stroke ikoner
```

## Läsordning

1. `HANDOVER-oversikt.md` — alla beslut, datakällor, acceptanskriterier
2. `screenshots/*.png` — visuella referenser
3. `src/oversikt.jsx` + `jobbpilot-v3.css` (sök "Översikt") — exakt implementation
4. `HANDOVER-v3.md` — om något i design-systemet (tokens, typografi) behöver slås upp

## Snabbfakta

- Route: `/oversikt` (default efter login)
- Stack: Next.js App Router, RSC default, client-islands för dismiss-state
- Datakälla: blandning av riktig BE och mock — se §3 i `HANDOVER-oversikt.md`
- Designton: civic utility (myndighetston) — inte SaaS-dashboard

Frågor → Klas (produktägare).
