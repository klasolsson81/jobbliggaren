# Claude Code-setup — JobbPilot

Denna fil förklarar hur Claude Code är konfigurerat i JobbPilot och hur Auto mode-klassificeraren samverkar med allow/deny/ask-listorna.

---

## 1. Körsätt

Klas kör Claude Code i **Auto mode** (kontinuerlig autonom exekvering). Det innebär att Claude inte frågar om tillstånd för varje enskild åtgärd — klassificeraren avgör i realtid vad som är säkert att köra utan mänsklig bekräftelse.

För att aktivera Auto mode: tryck `Shift+Tab` i Claude Code tills "auto" visas i statusindikatorn, eller använd `/auto`.

---

## 2. Hur allow/deny/ask samverkar med klassificeraren

Claude Code har tre lager av behörighetskontroll som tillämpas i ordning:

### 2.1 `deny` — hård blockering

Kommandon i `deny`-listan blockeras **alltid**, oavsett läge. Claude Code vägrar exekvera och informerar användaren. Används för destruktiva eller irreversibla operationer:

- `rm -rf`, `sudo`, `force push`, `terraform destroy`
- AWS delete-operationer, KMS nyckelradering
- Databasvolym-droppar

Dessa är absoluta — de kan inte överridas av Auto mode.

### 2.2 `ask` — kräver bekräftelse

Kommandon i `ask`-listan pausar Auto mode och ber om explicit godkännande, även om Auto mode är aktiverat. Används för åtgärder som påverkar delade system eller är svåra att ångra:

- `git push` — publicerar kod till remote
- `gh pr create/merge` — skapar eller mergar pull requests
- `terraform apply` — ändrar infrastruktur
- `dotnet ef database update` — kör migrations mot riktig databas
- `Write/Edit BUILD.md`, `CLAUDE.md`, `DESIGN.md` — ändrar projekt-fundamenten

Motivet för spec-filerna: `deny` innebär "aldrig", vilket blockerar även legitima ändringar som Klas explicit begär. `ask` innebär "bekräfta varje gång" — rätt nivå för filer som styr hela projektet.

### 2.3 `allow` — automatiskt godkänt

Kommandon i `allow`-listan körs direkt i Auto mode utan paus. Inkluderar:

- Alla läsoperationer (Read, Grep, Glob)
- Skrivoperationer på vanliga kodfiler
- `dotnet build/test/run`, `pnpm`, `git status/diff/log/commit`
- Docker compose (dev-stack)
- AWS read-only kommandon (describe, list, get)
- Terraform read-only (plan, show, state list)

### 2.4 Klassificeraren i Auto mode

Auto mode-klassificeraren är ett separat lager ovanpå allow/deny/ask. Den bedömer i realtid om en åtgärd är säker baserat på kontext — inte bara kommandonamnet. En åtgärd som inte matchar någon regel i allow/deny/ask bedöms av klassificeraren.

Klassificeraren pausar Auto mode och ber om bekräftelse om den bedömer en åtgärd som:
- Destruktiv (radering, formatering, överskrivning)
- Irreversibel (publicering, external API-anrop med sidoeffekter)
- Utanför förväntad kontext (ovanlig filväg, okänt kommando)

---

## 3. Konfigurationsfiler

### `settings.json` (committad)

Team-delad konfiguration. Innehåller allow/deny/ask-listor, modell (`claude-sonnet-4-6` som default), `effortLevel: high`, och `autoMemoryDirectory`.

**Viktigt:** `settings.json` är committad och påverkar alla som klonar repot. Ändringar kräver PR-diskussion.

### `settings.local.json` (gitignored)

Personliga overrides som läses **efter** `settings.json`. Arrays konkateneras, skalarer skrivs över. Kopiera från mallen:

```bash
cp .claude/settings.local.json.example .claude/settings.local.json
```

Typiska personliga tillägg:
- `AWS_PROFILE` om du använder annat profilnamn
- Extra allow-regler för personliga verktyg (`psql`, `redis-cli`)

Committa **aldrig** `settings.local.json`.

---

## 4. När ska Auto mode stängas av?

Stäng av Auto mode (växla tillbaka med `Shift+Tab`) när du:

- Debuggar ett okänt problem och vill följa varje steg
- Arbetar med säkerhetskritisk kod (auth, BYOK, GDPR)
- Är osäker på konsekvenserna av en åtgärd och vill tänka igenom den

Auto mode är optimerat för väldefinierade uppgifter med känd kontext. För explorativa sessioner är manuellt läge bättre.

---

## 5. Relaterade dokument

- [local-dev-setup.md](local-dev-setup.md) — Docker Compose-stack för lokal utveckling
- [BUILD.md](../../BUILD.md) — Produktspecifikation och tech-stack
- [CLAUDE.md](../../CLAUDE.md) — Kodningskonventioner för Claude Code
