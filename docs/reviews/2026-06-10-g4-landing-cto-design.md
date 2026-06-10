# G4 landing-redesign — CTO-riktning + design-reviewer

**Datum:** 2026-06-10 · **Branch:** `feat/design-g4-landing-redesign` · **Klas-direktiv:** "gröna boxen ful, snyggare landingpage" (Klas valde "CC tar fram förslag")

---

## senior-cto-advisor — Riktning A (produkt-forward ljus hero)

**Dom:** Riktning A med bindande modifiering (statisk ren-RSC produkt-peek, ej interaktiv).

**Motivering:** Klas-klagomålet är en princip-diagnos: nuvarande hero bryter regel 1 (papper inte glas — grön gradient-box bakom H1), regel 3 (login-FORMULÄR på hero = fyllnadselement; `/logga-in` finns redan = DRY-brott, Hunt/Thomas), och missar Mercury-doktrinen ("produkten ÄR gränssnittet"). A renodlar landing till sitt ansvar (konvertering + orientering), delegerar auth dit auth bor (SRP, Martin kap. 7), och fyller den frigjorda ytan med en produkt-peek som VISAR produkten.

**Sex delbeslut:** (1) Riktning A; (2) login → topbar-länk `/logga-in` (befintlig `.jp-land-top__link`); (3) statisk peek (fallback: ren typografisk hero om ej billig); (4) grönt = accent + ETT scoped gradient-fragment i peeken, ingen grön box bakom H1; (5) Features+footer ORÖRDA (KISS, scope-disciplin); (6) topbar behåll live-stats + lägg login-länk, mobil döljer stats men behåller login.

**Avvisade:** B (förfinad grön box — löser symptom ej princip-rot, snabblösning mot Mastercard-testet); C (tvåton — närmast, dokumenterad fallback om peeken känns för mycket, men splittrar grön identitet till konkurrerande band).

**Två Klas-preview-flaggor (medvetna val, ej missförstånd):** peek vs typografisk hero; login helt bort från `/`.

## design-reviewer — ✓ APPROVED (0 VETO / 0 Major / 2 Minor FYI)

Rendered light+dark + diff + token-verifiering. "Genuin civic-utility-uppgradering, inte bara en färgändring."

- **Snyggare + civic:** grön box bort, ljus canvas-hero, typografi bär tyngden (papper-inte-glas), Mercury-doktrin uppfylld via peek.
- **Green-usage korrekt (regel 5):** accent på primär-CTA-fill + topbar-login + peek-fragment; ingen grön box; knapp-kontraktet (accent-800-fill vit text = grön solid på ljus canvas, RÄTT). Inline-token-style-hacken ersatt av riktiga `.jp-btn--primary/--secondary` (renare).
- **Peek smakfull:** flat papper-kort, hairlines, mono ID/datum, scoped gradient-band; `aria-hidden` rätt (dekorativ produkt-illustration).
- **Login-flytt tydlig:** `<a href="/logga-in">` (ej button), mobil behåller länken.
- **Dark likvärdig:** peek-titlar #6EE7A8 ~7:1, topbar light-pinnad (login-hover #15603F undviker AA-fail).
- **Raderingar rena:** AuthCard/oauth-mark bort, LoginForm orört (`/logga-in` intakt), tester uppdaterade samma commit, 716 vitest gröna.
- **A11y:** fokusring grön på CTA (ljus canvas), tangentbordsnav, WCAG AA båda teman.

**Minor (FYI, ingen åtgärd):** (1) peek kan drifta från riktiga /jobb över tid (statisk per CTO-spec — framtida konsistens-watch); (2) peek-kicker dupликerar kicker-mönstret (opportunistisk delad primitiv vid nästa hero-touch).

**Mergeklar.** Klas rendered-GO kvarstår (Vercel-preview light+dark).
