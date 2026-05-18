# HANDOFF — dator-byte laptop → stationär (post-FAS-3 polish klart)

> **Engångs-handoff (Klas-auktoriserat §1.5-undantag för dator-byte).**
> **Stationära maskinen: läs detta, RADERA sedan filen** (`git rm docs/HANDOFF.md`)
> i nästa docs-commit så repot hålls rent. Continuation-prompt, ej permanent dok.

Klas fortsätter på stationär dator. Allt nedan är verifierat on-disk/on-origin
vid handoff (2026-05-18), inte gissat. **Detta är ett nära-rent stopp** — allt är
committat och pushat, ingen halvgjord kod. Det finns EN öppen punkt (Klas
live-verify) + nästa strategiska steg (Fas 4, Klas-GO).

---

## 1. Förkrav (kör först)

1. `git fetch origin && git status` — **förväntat: HEAD = `<denna handoff-commit>` = origin/main, ren tree** (efter pull). 0 unpushed.
2. `git log --oneline -10` → topp ska innehålla: `<handoff-commit>`, `850ae37 fix(web): tabular-nums`, `40a413a fix(web): slot-map incident-fix`, `3d09bf6 Revert eece124`, `eece124 feat list-skannbarhet`, `2413de7 docs ADR 0041-amendment`, `9b00c0f feat --jp-border-structural`, `423a6f1 docs FAS 3 stängd`.
3. **MEMORY.md finns på stationär** (saknades på laptop — laptop-sessionen läste disciplinreglerna ur den inkommande HANDOFF.md). Läs hela `MEMORY.md` på stationär. Särskilt: `feedback_cto_decides_multi_approach`, `feedback_nonstop_with_pr_reports`, `feedback_pathspec_commit_parallel_cc`, `project_dev_test_account`, `feedback_spec_edit_approve_classifier_block`, `feedback_td_lifting_discipline`.
4. **dev-test-creds:** stationär använder sitt **egna befintliga** `%USERPROFILE%\.jobbpilot\dev-test-creds.env` (det ursprungliga kontot). OBS: ett **2:a** syntetiskt dev-test-konto skapades på laptop (sanktionerat `/register`-mönster, runbook §92/§115) — dess creds finns ENDAST i laptop-maskinens fil, aldrig i repo/chat. Stationär ska INTE försöka använda laptop-kontot; använd ditt eget. Fixtur-ansökningar från laptop-kontots visual-verify-körningar ligger kvar i dev-DB (syntetiskt, ingen PII — acceptabelt per runbook).
5. Docker (Testcontainers vid backend), .NET 10 SDK, Node 22+/pnpm, `dotnet tool restore`, AWS SSO (ej krävs förrän ev. deploy-uppföljning).

## 2. Mandatory reads

- `CLAUDE.md` (hela — särskilt §1.5/1.6, §2 Clean Arch/DDD/CQRS, §4.3 RSC/client-boundary, §9.2/9.6 agent-invocation & in-block-vs-TD, §12 token-disciplin).
- `docs/current-work.md` (status-header — post-FAS-3-spåren + enda öppna punkt).
- `web/jobbpilot-web/AGENTS.md` — **NY permanent gate:** `pnpm build` obligatorisk pre-push för RSC/client-boundary-ändringar (lades till efter prod-incidenten denna session — läs den, den gäller framåt).
- ADR `0046` (FAS 3 scope-redefinition, **Accepted** 2026-05-18), `0041` (**amended 2026-05-18** — `--jp-border-structural`, Accepted), `0047` (design-reviewer Area 5-mandat), `0048` (cross-aggregat-join).
- `docs/sessions/2026-05-18-1009-fas3-stangning.md` (FAS 3-stängningskontext).
- Reviews denna session: `docs/reviews/2026-05-18-fas3-stopp3b-area5-veto.md` (v1→v2 PASS), `2026-05-18-dark-border-structural-contrast-design.md` (dark-kant + Gate 2 GODKÄND), `2026-05-18-ansokningar-list-skannbarhet-area5.md` (Area 5 GODKÄND 0/0/2).

## 3. Var vi är — exakt nuläge

**FAS 3 (Application Management) FORMELLT STÄNGD 2026-05-18** (`423a6f1`; ADR 0046 Proposed→Accepted; defer-not för jobad-kopplad-dark = bekräftad Chromium/CDP-instrumentartefakt, produktkod invariant, Klas browser-toggle = auktoritativ).

**Två Klas-begärda post-stängnings-polish-spår LEVERERADE denna laptop-session:**

1. **Dark-kantlinje-kontrast** (`9b00c0f` feat + `2413de7` docs) — Klas såg dark-kanter "alldeles för svart" på laptop. CTO Approach B: ny roll-token `--jp-border-structural` (dark `#64748B` ≈3.6:1 WCAG 1.4.11, light `#E2E8F0` oförändrad), 13-posters kirurgisk migrering (kort/sektion/panel/sidebar; dekorativa hairlines bevarade), ADR 0041-amendment, Klas körde `approve-spec-edit.sh` (DESIGN.md/contrast-table/tokens-full synkade). design-reviewer Gate 2 GODKÄND 0 fynd. **Klas dark-toggle i egen browser bekräftade dark fungerar live.** Levererat & pushat.

2. **`/ansokningar` list-skannbarhet** (statusöversikt alla-10-inkl-0-count + minimera/maximera-grupper). CTO 6-punkts-ram (a8e269eb): RSC page.tsx + client-ö `ApplicationsPipeline`, ren in-page-ankarnav, alla expanderade default, ingen persistens (YAGNI). **PROD-INCIDENT:** `eece124` bröt prod (render-prop-funktion icke-serialiserbar över RSC→client, ERROR 850043857; vitest 11/11 fångade ej — jsdom isolerar gränsen). CTO Approach A: `git revert eece124` → `3d09bf6` (prod återställd ~1-2 min) → proper fix `40a413a` (serialiserbar `Record<ApplicationStatus,ReactNode[]>` slot-map, **CC oberoende `pnpm build` GRÖN**) → Minor-1-polish `850ae37` (`tabular-nums`). design-reviewer Area 5 **GODKÄND 0 Block/0 Major/2 Minor** (Minor 1 in-block-fixad; Minor 2 valfri tooling ej; render-validerings-lucka tom-status-fixtur = unit-test-täckt, design-reviewer "ej fynd, ej krävs för stängning"). Levererat & pushat, live.

**Process-arv:** `pnpm build` permanent obligatorisk pre-push-gate för RSC/client-boundary, kodifierad i `web/jobbpilot-web/AGENTS.md`. Incidenten = gate-lucka stängd, **ej disciplin-regression** (CTO + ADR 0019 trigger 3 ej uppfylld; process följdes, gaten saknades).

## 4. ENDA ÖPPNA PUNKT

**Klas bindande live-verify av `/ansokningar` list-skannbarhet.** `850ae37` är live på `https://www.jobbpilot.se/ansokningar`. Klas skulle verifiera i browser (statusöversikt + minimera/maximera, laptop light+dark) men bytte till stationär dator innan. **På stationär: be Klas live-verifiera i sin browser** (eller han gör det självmant). Vid OK → spåret stängt, inga fler steg. Vid fynd → ny runda (CTO/nextjs-ui-engineer per §9.6). design-reviewer Area 5 är redan GODKÄND — detta är Klas slutgrind, ej blocker för annat.

Post-stängnings-backlog är **UTTÖMD** (båda spåren levererade). Inga andra öppna spår.

## 5. Nästa strategiska steg — Fas 4 (AI Layer)

Kräver **egen strategisk Klas-GO för sessionsbyte (§9.2) + ren `/clear`**. Påbörjas INTE automatiskt. Härdad startprompt (levererad i laptop-chatten 2026-05-18):

```
Fas 4 (AI Layer) — sessionsstart. Strategisk Klas-GO för sessionsbyte given (§9.2).

Förkrav: git fetch && git status (HEAD = origin/main, ren); git log --oneline -6;
docker compose up -d; dotnet tool restore; AWS SSO vid deploy.

Mandatory reads: CLAUDE.md (hela — §5.3 AI-layer anti-patterns, §2 Clean Arch,
§9.2/9.6); BUILD.md §18 Fas 4-milstolpe + AI-sektioner; docs/current-work.md;
docs/steg-tracker.md rad 33 (Fas 4 Planerad); ADR 0040 (smart CV-filter Proposed
— Fas 4-detaljdesign), ADR 0045 (perf-budgetar); /prompts/*.prompt.md
(prompt-bibliotek, ai-prompt-engineer äger); web/jobbpilot-web/AGENTS.md
(pnpm build-gate RSC/boundary). MEMORY.md (hela).

Första uppgift: plan-design Fas 4-scope MED Klas innan kod (§6.3 granskningsspärr
1) — AI-features end-to-end + 14 dagars dogfood (BUILD.md §18). EU-inferens via
Bedrock för systemnyckel (§5.3, GDPR). Ingen kod innan scope/sekvens/risker
designade i chatt.
```

## 6. Disciplin (agenter INLINE, gäller framåt)

senior-cto-advisor (decision-maker multi-approach/incident/in-block-vs-TD — CC ger ej egen rek) · design-reviewer (Area 5 render-VETO mot bilder, ADR 0047, bindande) · adr-keeper (ADR-skapande/amendment vid Klas-GO) · nextjs-ui-engineer (frontend bygg) · test-writer (.NET-ONLY xUnit — EJ frontend; React = nextjs-ui-engineer + vitest co-lokaliserat) · code-reviewer (>5 filer) · security-auditor (PII/auth) · docs-keeper (session-end synk). `git commit -- <pathspec>` enda form, verifiera `git show --stat HEAD`. **`pnpm build` obligatorisk före push vid RSC/client-boundary** (AGENTS.md). Tag/deploy-push + spec-edit + ADR-flip = Klas-GO/Klas-STOPP (auto-mode-klassificeraren blockerar push utan tydligt GO — korrekt by-design). Non-stop med rapport efter varje push. Klas approve-spec-edit.sh för BUILD/CLAUDE/DESIGN.md (single-use-token).

## 7. Förbud / utanför scope

Fas 4/AI (egen Klas-GO + /clear). Ändra ej BUILD.md/CLAUDE.md/DESIGN.md utan Klas approve-spec-edit.sh. Ingen ny top-level-dep utan BUILD.md §3.1 + Klas-GO. Ingen revert/history-rewrite av pushad delad main (forward-recovery, ADR 0019). Påbörja inget nytt spår utan Klas-GO — backlogen är uttömd.

## 8. Förväntat sluttillstånd efter pull

HEAD = `<handoff-commit>` = origin/main, ren tree. Klas live-verifierar `/ansokningar` list-skannbarhet i sin browser (enda öppna punkt). `git rm docs/HANDOFF.md` i nästa docs-commit. Därefter: invänta Klas strategiska GO för Fas 4 (ren /clear) — inget annat öppet.

## 9. Avslutning

Nära-rent stopp: allt committat & pushat, prod grön (`/ansokningar` fungerar live efter incident-fix), två polish-spår levererade & review-godkända. Kvar = Klas slutverify (icke-blockerande) + strategisk Fas 4-GO. Inga TDs, inga öppna incidenter.
