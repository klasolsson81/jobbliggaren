# ADR 0054 — Header-meny ersätter sektionerad sidebar

**Datum:** 2026-05-19
**Status:** Accepted
**Kontext:** JobbPilot v3 UI-refactor (HANDOVER-v3.md §0.1-veto, §6, §9). Shell-navigationen ska vara en header-meny — ingen sidebar/burger på desktop.
**Beslutsfattare:** Klas Olsson (produktägare; explicit Accepted-flip-GO 2026-05-19)
**Supersedes:** [ADR 0037](./0037-design-system-v2-slate-dark-mode.md) — shell Variant B (sektionerad vänster-sidebar 240px)
**Relaterad:** ADR 0052 (v3 designsystem — header-tokens), ADR 0028 (admin-authorization — Granskning-länkens placering); ADR 0037 dark-mode-mekanism (`data-theme="dark"`) **består** (ej superseded); HANDOVER-v3.md §0.1/§6/§9; målbild 01–04

> **Livscykel-/proveniens-not:** Skriven 2026-05-19 av Claude Code (adr-keeper)
> på explicit Klas-begäran — medveten override av CLAUDE.md §9.4
> webb-Claude-verbatim-konventionen (memory `feedback_klas_can_override_adr_verbatim_source`).
> Besluts-substansen är transkriberad från HANDOVER-v3.md (auktoritativ
> designspec med §0-veto) + senior-cto-advisor-dom Fas 0 (Beslut 4). Inga
> nya beslut konstruerade. Status **Accepted** per Klas explicit
> Accepted-flip-GO 2026-05-19.

---

## Kontext

HANDOVER-v3.md §0.1 är ett veto: shell-navigationen ska vara en header-meny.
Ingen sidebar och ingen burger på desktop. ADR 0037 specade tidigare shell
Variant B — en sektionerad vänster-sidebar på 240px. v3-målbilderna 01–04
visar header-driven IA.

ADR 0037:s dark-mode-mekanism (`data-theme="dark"`) är ortogonal mot
shell-layouten och påverkas inte av detta beslut.

## Beslut

### Beslut 1 — Header-shell (desktop)

- Höjd 68px, vit bg i **båda** teman (scoped token-override per ADR 0052
  Beslut 6), sticky, 1px border-bottom.
- **Vänster:** brand (J-monogram + ordmärke), länk till landing.
- **Mitten:** nav — Jobb · Mina ansökningar · CV.
- **Höger:** notiser-bell + avatar. User-menu från avatar: Inställningar /
  Senaste sökningar / Mina CV / — / Logga ut.

### Beslut 2 — Mobil (<900px)

- Nav döljs.
- Burger → drawer från **höger**, bredd `min(340px, 88vw)`.
- Drawer innehåller samma länkar + Inställningar.

### Beslut 3 — Ingen sidebar

Ingen sidebar renderas under något skäl (direkt veto-efterlevnad §0.1).
Admin-länken (Granskning) flyttas till user-menu/drawer (korsref ADR 0028
admin-authorization — placering ändrad, behörighetsmodell oförändrad).

## Konsekvenser

### Positiva

- Matchar v3-målbild 01–04.
- Enklare mobil-IA — en drawer med samma länkar, ingen sektionerad
  sidebar-hierarki att tappa orienteringen i.
- Header-only shell minskar layout-yta och förenklar responsiv brytpunkt.

### Negativa + mitigering

- All sidebar-CSS/DOM rivs (`jp-sidebar`, `jp-nav`-sektioner). Mitigering:
  ADR 0037 shell Variant B explicit superseded — ingen tvetydighet om vilket
  shell som gäller; rivning sker i refactor-fas, ej tyst.
- Admin-länken (Granskning) byter plats (sidebar → user-menu/drawer).
  Mitigering: behörighetsmodell (ADR 0028 marker-interface + HTTP-policy)
  oförändrad — endast presentationsplaceringen flyttas.

## Alternativ övervägda

### Alternativ A — Header-meny (valt)

Se Beslut 1–3.

**Valt:** enda varianten som efterlever §0.1-vetot och matchar målbild
01–04. (Källa: senior-cto-advisor Beslut 4; HANDOVER §6.)

### Alternativ B — Behåll sektionerad sidebar (ADR 0037 Variant B)

Behåll 240px vänster-sidebar.

**Avvisat:** direkt veto-brott mot HANDOVER §0.1 / §9. (Källa:
senior-cto-advisor Beslut 4.)

## Implementationsstatus

- **Beslut accepterat 2026-05-19** (Klas Accepted-flip-GO).
- Implementation: JobbPilot v3 UI-refactor (header-shell + responsiv drawer,
  rivning av `jp-sidebar`/`jp-nav`-DOM/CSS, Granskning-länk flyttad till
  user-menu/drawer). `pnpm build`-gate per AGENTS.md.
- ADR 0037 markeras i index som delvis superseded av denna ADR (shell
  Variant B); dark-mode-mekanismen i ADR 0037 förblir Accepted och orörd.
