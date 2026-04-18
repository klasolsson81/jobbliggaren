# .claude/

Konfiguration för Claude Code i detta repo.

Klas kör Auto mode. Se [docs/runbooks/claude-code-setup.md](../docs/runbooks/claude-code-setup.md) för hur klassificeraren interagerar med listorna.

## Filer

| Fil | Syfte |
|---|---|
| `settings.json` | Team-delad konfiguration (committad) |
| `settings.local.json` | Personliga overrides (gitignored, kopiera från `.example`) |
| `settings.local.json.example` | Mall för personlig konfiguration |

## Kataloger (gitignored)

| Katalog | Innehåll |
|---|---|
| `auto-memory/` | Automatisk minne mellan sessioner |
| `logs/` | Session-loggar från hooks |
| `backups/` | Pre-compact snapshots |
| `tasks/` | Aktiva uppgiftslistor |
