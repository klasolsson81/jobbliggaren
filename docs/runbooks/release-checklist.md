# Release-checklist (generisk, återkommande)

> Repeterbar release-procedur för JobbPilot. Gäller **varje** tag-driven
> release, oavsett fas. Skild från `v0.2-prod-launch-checklist.md` — den är
> en engångs-checklist för *första* prod-deployen; detta är den löpande
> rutinen som används om och om igen.
>
> **Skapad:** 2026-05-17 (roster-gap-CTO 2026-05-17 §1.5 — "runbook, inte
> release-manager-agent"; ADR 0045-bunt steg 6). Deploy-beslut är strategiska
> och kräver Klas-godkännande (CLAUDE.md §9.2) — denna runbook ersätter inte
> det, den strukturerar det.

---

## 1. Tag-semantik (ADR 0019)

| Tag-mönster | Miljö | Approval | Exempel |
|---|---|---|---|
| `v*-dev` | dev | Automatisk (deploy-dev.yml) | `v0.3.1-dev` |
| `v*-rc*` | staging | Automatisk till staging | `v0.3.0-rc1` |
| `v*` (ren) | prod | **Manuell approval (Klas)** | `v0.3.0` |

`main` är enda branch (ADR 0019, direct-push). Staging är *miljö*, inte
branch. Deploy sker via tag-push på `main`, aldrig via branch-merge.

---

## 2. Före tag (pre-flight)

- [ ] **main-CI grön** — `gh run list --workflow build --limit 1` → `success`
      (backend + frontend + coverage + ci alla gröna). Coverage-gaten
      (ADR 0044) får inte vara röd.
- [ ] **Observe-only-signaler granskade** (ADR 0045) — `lighthouse` /
      `loadtest` / `audit`-jobben är observe-only och blockerar inte, men
      deras `::warning::`/summary ska läsas inför release: ny CWV-regression,
      p95-budget-överskridande eller High/Critical-CVE noteras och bedöms
      (åtgärda eller medvetet acceptera + motivera).
- [ ] **Inga öppna Klas-STOPP-flaggor** i `docs/current-work.md`.
- [ ] **Aktiva Major-TD mot release-scope** genomgångna (`docs/tech-debt.md`)
      — launch-blocker-TD löst eller medvetet deferrad med motiv.
- [ ] **Migrations** — om EF Core-migration ingår: verifiera schema-mode-
      dispatch (ADR 0033) och DB-roll-separation (ADR 0034); Identity-schema-
      ändring → manuell procedur (TD-72).
- [ ] **Kollations-version — ENDAST vid Postgres-image-bump eller major-uppgradering**
      (#884, ADR 0109). Ett btree-index på text är byggt **med** en kollation. Ändras
      kollationens *definition* under det — en ny ICU-version i basimagen, en ny glibc,
      en major-uppgradering — sorterar indexet efter en ordning som inte längre gäller.
      Postgres **kraschar inte** på det: frågorna blir bara tyst fel (rader hittas inte,
      `ORDER BY` ljuger). Detta gäller `en_US.utf8` **redan idag** (collversion 2.41);
      #884 skapade inte exponeringen, det är första gången repot **namnger** den.
      **Efter varje Postgres-image- eller major-bump, före tag:**
      ```sql
      -- 1. Har någon kollation drivit? (tom output = inget att göra)
      SELECT collname, collversion, pg_collation_actual_version(oid) AS faktisk
      FROM pg_collation
      WHERE collversion IS NOT NULL
        AND collversion IS DISTINCT FROM pg_collation_actual_version(oid);

      -- 2. Om någon rad kom tillbaka: bygg om berörda index och kvittera versionen.
      REINDEX DATABASE CONCURRENTLY jobbliggaren;   -- eller de berörda indexen
      ALTER COLLATION public.swedish REFRESH VERSION;
      ALTER DATABASE jobbliggaren REFRESH COLLATION VERSION;  -- för DB-defaulten
      ```
      **Kvittera INTE versionen (steg 2b) utan att först ha byggt om (steg 2a)** — det
      tystar varningen utan att laga indexen, vilket är strikt värre än att inte ha
      kollat alls.
- [ ] **Om en migration faller på `lock_timeout` — kör om den, det är säkert.** Migrationen
      som sätter kollationen (#884) tar ACCESS EXCLUSIVE och binder sin väntan till 3 s.
      Krockar den med en långkörande transaktion får du
      `canceling statement due to lock timeout` och **hela migrationen rullas tillbaka
      atomärt** (verifierat mot riktig Postgres med en konkurrerande AccessShareLock:
      avbrott efter 3001 ms, databasen orörd). Inget delvis applicerat tillstånd kan
      uppstå. Vänta ut den blockerande transaktionen — typiskt nattsynken — och kör om.
      Det är felläget guarden **finns** för: ett högljutt deploy-fel i stället för ett
      tyst läs-avbrott.
- [ ] **GDPR-konsekvens** för nytt scope bedömd (CLAUDE.md §8 punkt 8) — ny
      PII? loggning? retention? Audit-wire intakt (ADR 0035)?
- [ ] **Secrets-hygien** — inga nya secrets i klartext; gitignored
      `appsettings.Local.json` lokalt / managed secrets-store i ops + DEK-envelope
      (`IDataKeyProvider`, ADR 0066/0049) för allt känsligt (CLAUDE.md §5; AWS
      Secrets Manager + KMS rivet, ADR 0066).
- [ ] **Lokal diff-granskning** (CLAUDE.md §6.3 mekanism 4) — Klas läser
      `git log` + `git diff` för release-spannet.

---

## 2.5 HÅRD GRIND: Resend e-post-prod-flip (ADR 0080)

> Gäller ENDAST en release som aktiverar `Email:Provider=Resend` i non-dev
> (bakgrundsmatchnings-notiser). Tills dess kör `NullEmailSender` — ingen
> e-post skickas, och denna grind är inte relevant. Resend är en **US-processor**
> → mottagar-adress + opt-in-faktum är en tredjelandsöverföring. **Alla fyra
> punkter MÅSTE vara gröna innan `Email:Provider` flippas** (ADR 0080
> prod-flip-checklista). CC får ALDRIG flippa providern eller signera DPA:t.

- [ ] **1. Tredjelands-grund** — signerad **DPA** med Resend på fil +
      dokumenterad **SCC/adekvans**-grund + Resend-posten i
      `docs/runbooks/gdpr-processing-register.md` (ROPA, lokal) +
      **security-auditor-sign-off** på prod-e-post-konfigen. (DPA-signering =
      Klas; ROPA + sign-off = #183.)
- [ ] **2. TD-115** — legacy opt-OUT-default sanerad (#185 / PR #211 — **KLAR**).
- [ ] **3. TD-116** — consent-/disclosure-copy avslöjar e-postleverans för
      användaren (#186).
- [ ] **4. TD-114** — stranded-Queued-reaper (#184 / PR #212 — **KLAR**) +
      **Resend `Idempotency-Key`** på real-send-vägen (#187 / PR #230 — **KLAR**;
      VO `MatchNotificationIdempotencyKey`, ad-scoped Direct + content-hash Digest).

Källa: ADR 0080 §"Prod-Resend-flip pre-condition checklist"; ROPA-behandlingen
"Bakgrundsmatchnings-notiser via e-post (Resend)".

---

## 2.6 HÅRD GRIND: integritetspolicyns "planerat"-formuleringar (#852)

> Gäller **den första `v*`-taggen till prod** (och varje senare release som
> aktiverar en behandling policyn ännu beskriver som planerad). Detta är en
> **aktiverings**-händelse, inte en copy-händelse — därför bor den här och inte i
> en PR.
>
> **Läget idag är korrekt, inte trasigt.** Policyn beskriver ansökningshistorik/
> företagsöversikt, SCB-uppslag, Hetzner och Cloudflare som *"planerat och ännu
> inte i drift"*. Koden är skeppad till dev, men det finns ingen prod-deploy och
> inga registrerade — policyn styr den *driftsatta* tjänsten, och om den påstod
> drift nu hade den varit falsk i motsatt riktning. **Flippa alltså aldrig i
> förväg.** I samma sekund en `v*`-tagg går till prod blir meningarna falska, och
> en behandling som körs under en policy som förnekar att den körs är enligt
> ADR 0090 D3 *"unlawful-by-transparency-defect until the policy is honest"*
> (Art. 12/13). Konsekvensen är juridisk, inte kosmetisk.
>
> **Samma grind och samma ögonblick som #842** (Art. 17-raderingen) — checka av
> båda i samma cutover.

- [ ] **1. Inventera** — `grep -c "planerat och ännu inte i drift"
      web/jobbliggaren-web/messages/sv/content-legal.json` och
      `grep -c "This is planned and not yet in operation."
      web/jobbliggaren-web/messages/en/content-legal.json`. Vid 2026-07-25:
      **6 + 6**. Alla 12 måste bedömas — en behandling i taget, inte som klump:
      ansökningshistorik/företagsöversikt och SCB-uppslaget aktiveras av *koden*,
      medan Hetzner och Cloudflare aktiveras av *deployen*.
- [ ] **2. Flippa BARA det releasen faktiskt aktiverar.** Kvarstående "planerat"-
      meningar för behandlingar som fortfarande inte är i drift ska stå kvar.
      Släpper releasen ingen av dem är rätt utfall att inte ändra något.
- [ ] **3. Paritet sv + en** — båda språken i samma ändring (`privacy.sections`
      Art. 13-kategorilistan **och** avsnittet "Inga automatiserade beslut" bär
      formuleringen; missa inte den andra).
- [ ] **4. Bumpa `privacy.updated`** ("Senast uppdaterad: YYYY-MM-DD") i samma
      ändring, båda språken.
- [ ] **5. Lockstep** — flippen mergas och deployas **med** aktiveringen, aldrig
      före (då påstår policyn drift medan lanseringsgrindarna, bl.a. #842, inte
      passerats) och aldrig efter (då kör behandlingen under en policy som
      förnekar den).
- [ ] **6. security-auditor + design-reviewer** på copy-diffen (Art. 12/13
      + civil ton, CLAUDE.md §10) — det är en renderad juridisk sida.

Varför grinden bor här: plikten var tidigare spårad **enbart** i
`docs/decisions/0090-*.md` och en `docs/reviews/`-rapport — **båda gitignorerade**,
alltså osynliga för CI, för en PR-granskare och för en parallell CC-session
(#852:s acceptanskriterium 4). Den här filen är trackad, och Art. 30-registerposten
för ansökningshistorik (#853) bär samma **Statusgrind**-idiom som `resume_files`.

Källa: #852 · ADR 0090 D3 · #824 PR 4 (som kvalificerade golv-semantiken i samma
två stycken men medvetet inte flippade dem) · #842 (samma cutover).

---

## 3. Tagga + deploy

```bash
# Verifiera HEAD är exakt det som ska släppas
git log --oneline -1
git rev-parse HEAD

# dev/staging — automatisk efter push
git tag v<X.Y.Z>-dev <HEAD> && git push origin v<X.Y.Z>-dev      # → dev
git tag v<X.Y.Z>-rc1 <HEAD> && git push origin v<X.Y.Z>-rc1      # → staging

# prod — KRÄVER Klas-GO innan tag-push (CLAUDE.md §9.2)
git tag v<X.Y.Z> <HEAD> && git push origin v<X.Y.Z>             # → prod (manuell approval i pipeline)
```

CC får **inte** push:a en prod-tag (ren `v*`) utan explicit Klas-GO i
sessionen. dev/rc-tags är CC-tillåtna efter grön CI.

---

## 4. Efter deploy (verifiering)

> Hetzner-modell (ADR 0050/0066): hela stacken (API + Worker + Postgres + Redis +
> Caddy + Next.js) kör i Docker Compose på CAX31-boxen bakom Caddy. Konkreta
> service-namn/kommandon finalize:ras med **#196 / TD-106** (Compose-stack + proxy
> + härdning) — stegen nedan är på modell-altitud tills dess.

- [ ] **Compose-tjänster startar** (api + worker) — `docker compose ps` på boxen
      visar dem `healthy` (konkret service-namn/compose-fil: #196/TD-106).
- [ ] **`/api/ready` → 200** mot målmiljöns domän (strict readiness: DB +
      Redis dependency-checks, TD-29).
- [ ] **`/api/health` → 200** (liveness).
- [ ] **Hangfire-jobben** kör enligt schema om release rör Worker
      (`*/10`-cron etc.) — verifiera i Hangfire-dashboard/loggar.
- [ ] **Audit-wire** — om release rör audit-genererande flöden: bevisa
      INSERT i `audit_log` via den strukturerade logg-sinken (MEL → Seq; full
      prod-sink = TD-104) + direkt `audit_log`-query (ADR 0035).
- [ ] **Ops-signaler granskade** — health-checks + extern uptime-monitor
      (UptimeRobot/BetterStack, ADR 0050 — ersätter ALB/CloudWatch-health);
      jobtech-sync-/auditor-write-/log-pipeline-health läses via logg-sinken.
      Konkret alerting-konfig: #196/TD-106 + TD-104.
- [ ] **Frontend** (om i scope) — Lighthouse observe-signal mot
      ADR 0045-budgetar; manuell rök-test av kritiska flöden.
- [ ] **Rollback känd** — återställ föregående byggda image-tag via Compose
      (se §5); konkret procedur #196/TD-106.

---

## 5. Rollback

Vid fel efter prod-deploy (Hetzner-modell, ADR 0050 "Rollback" amenderat
2026-06-08 — AWS-stacken är riven, ADR 0066):

```bash
# På CAX31-boxen: pinna image-taggen tillbaka till föregående release och
# re-deploya Compose-stacken. Samma image-byggväg som prod (next build / dotnet
# publish körs i CI → enbart den byggda imagen skickas till boxen), så den lokala
# Docker-Compose-stacken är dev/prod-paritets-baselinen vid en misslyckad cutover.
IMAGE_TAG=<föregående-release> docker compose up -d
# Konkret tag-mekanism + service-namn finalize:ras med #196/TD-106 (ADR 0050).
```

Notera incidenten i `docs/sessions/` + relevant runbook. Skapa ADR om
rollback avslöjar ett arkitekturellt problem (CLAUDE.md §8 punkt 9).

---

## 6. Efter release (docs-synk)

- [ ] `docs/current-work.md` — status uppdaterad (CLAUDE.md §1.5).
- [ ] Session-logg i `docs/sessions/` om release var en egen session.
- [ ] `docs/steg-tracker.md` om STEG flyttat status.
- [ ] Tag + miljö noterad så nästa release vet senaste prod-state.

---

## Referenser

- ADR 0019 (direct-push + tag-semantik), ADR 0033/0034 (migrations/DB-roller),
  ADR 0035 (audit-wire), ADR 0050 (Hetzner-deploy: CAX31 + Caddy + Compose +
  rollback-modell) / ADR 0066 (AWS-exit), ADR 0036 (ops-alarms — supersederad av
  ADR 0050:s health-check/uptime-monitor-modell), ADR 0044 (coverage-gate),
  ADR 0045 (perf observe-only-signaler); TD-106 (konkret Compose-stack) / TD-104
  (logg-sink/observability)
- CLAUDE.md §6.3 (granskningsspärrar), §8 (DoD), §9.2 (deploy kräver Klas-GO)
- BUILD.md §15 (deployment/rollback)
- `docs/runbooks/v0.2-prod-launch-checklist.md` — engångs-checklist för
  *första* prod-deployen (komplement, inte ersättning för denna)
