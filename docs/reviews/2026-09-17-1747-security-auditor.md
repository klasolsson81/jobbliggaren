# security-auditor — #1747 (epic #1732 part 6d)

- **PR:** #1752 · **Branch:** `chore/drop-auth-provider-1747` · **Base:** `d90414f3`
- **Round 1 against:** `5ec3a1e6` · **Scoped re-check against:** `62c169f0`
- **Verdict round 1:** ✓ Approved — 0 Blocker / 0 Major / 3 Minor
- **Verdict re-check:** ✓ **Approved — 0 Blocker / 0 Major against the final diff `62c169f0`**
- **Escalation to Klas:** YES — verbatim below, unchanged by the delta

## Round 1

### Minor 1 — the Identity context has no automatic apply path, and the parked row that owns it has now fired

`deploy/docker-compose.yml:493` · `src/Jobbliggaren.Migrate/Program.cs:333-334` ·
`docs/runbooks/release-checklist.md:43-45`

The `migrate` service runs `command: ["schema"]`, and `schema` is by its own comment
*"AppDbContext only"*. Identity migrations are applied only by `migrate bootstrap` with master
credentials — explicitly *"an operator step"*. Measured: a grep for `migrate bootstrap` over
`deploy/`, `docs/runbooks/`, `scripts/`, `.github/` returns **zero** invocations.
`release-checklist.md:43-45` owns the gap and says *"Identity-schema-ändring → manuell procedur
(parkerad, #1172)"* — #1747 is the first Identity schema change since the box was provisioned,
so the condition is no longer hypothetical. No new issue — #1172 already carries the row.

### Minor 2 — the drop is irreversible for data, and the emptiness is derived from the writers, not read from the database

`Down` restores structure, never values. That no values exist is measured on the **writer**
side, not read off values this session. Requires a dated read-only line before `bootstrap`:
`SELECT count(*) FROM identity."AspNetUsers" WHERE provider_user_id IS NOT NULL;` → expect `0`.
A read on the box needs no GO. Defense-in-depth, not distrust: if the derivation were wrong the
consequence is bounded — the only registered accounts are the controller's own — and the nightly
backup carries `identity`, so a wrong apply is restorable.

### Minor 3 — two XML-doc claims in the migration test do not match their sources

(a) *"…lets `AppIdentityDbContext`'s migrations run as the role production migrates with rather
than as the container's superuser."* Production migrates the **Identity** context with **master
credentials** (`Program.cs:346-363`, `RunBootstrapAsync`). (b) Line 44 cites `d90414f3`; the
amendment itself writes `081e4c67`. The misdirection in (a) is toward the safe side — the app
role has less privilege than master, so green under app implies green under master, and the
oracle holds.

### Answers to the session's questions

1. **Exposure — decreases, structurally.** `provider_user_id` was a nullable column shaped for an
   external IdP's subject id (Art. 4(1) identifier) on the table holding every account, with a
   UNIQUE index on top. Dormant was a **convention** (no writer), not a barrier.
2. **Art. 5(1)(e) does not carry — do not write it that way.** Nothing was stored too long,
   because no personal data was in the columns to time-bound. Art. 5(1)(c) carries marginally.
   **The real gain is Art. 25(1)/(2)** plus **Art. 5(2)**. **No Art. 30 gap** — the register
   strike is correct and complete, and no neighbouring sentence became false: the entry was never
   an exhaustive column list. The window between the strike and the manual apply creates no
   Art. 30 error: Art. 30(1)(c) binds **categories**, and `provider` already fell within the
   category the entry names ("kontometadata"), which is unchanged.
3. **Nothing 6a needs is dropped. Measured, not assumed.** `AspNetUserLogins` exists since
   `InitialIdentity`, with `pk_asp_net_user_logins` on `(login_provider, provider_key)` and
   `ON DELETE CASCADE`. The takeover-relevant invariant — one (provider, subject) maps to at
   most one account — survives and is **stronger** than the dropped filtered index. What
   deliberately falls is the reverse constraint (at most one external login per account), which
   the amendment writes out as a decision, not an oversight.
4. **Nothing in ADR 0024/0050 or the register is falsified.** ADR 0024 never names the columns;
   the exhaustive *"Every other UNIQUE index…"* sentence is no longer in the ADR at all. The drop
   runs **opposite** to falsification: read as exhaustive, the inventory becomes unambiguously
   complete once the omitted candidate ceases to exist.

**Audit area 8 (the suppression surface): NOT triggered — explicitly.** Measured against the
diff's file set: all nine files are `.cs` under `src/…/Identity/` and `tests/`. Zero files under
`web/`, `.github/`, `Directory.Packages.props`, `*.csproj/props/targets`, `global.json`,
`pnpm-lock.yaml`, `pnpm-workspace.yaml`.

## Scoped re-check (`5ec3a1e6..62c169f0`)

Delta surface measured, not judged: exactly two files, both under `tests/`. **Zero changes under
`src/`** — the migration blob is byte-identical in both commits
(`3cbaca081f03f39bc84f495463b6c82bad5110b4`).

**Minor 3 — CLOSED, both halves, each measured separately.** The new posture clause was reviewed
**as new text, not waved through as her own**: `TestDatabaseProvisioner.cs:136-139` returns a
connection string with `Username = Roles.App`; `Program.cs:346-363` applies the Identity
migrations with `masterCs`. "Stricter" is the right word and the right direction.

**Graded in the delta and not flagged by the session:** the `MappedPlaintextExposureRegistry`
change is correct and necessary, verified in three steps — the neighbouring `AspNetUserLogins`
entry stays true, no mechanical gate breaks, and the length gate clears at 277 chars against a
60-char floor (counted, not estimated).

> ⚠ **This is the one place my own first round was too narrow, and I write it out rather than
> let the next reviewer redo the sweep.** My greps were keyword-based — `AuthProvider`,
> `ProviderUserId`, `provider_user_id`, `ix_asp_net_users_provider` — and this entry described
> the column in **prose** without naming it. An identifier grep never reaches such a line.
> **Lesson for the next PII-column drop: sweep prose descriptions for the concept, not only the
> source-code name.**

**AGENTS.md §5 `Tests:` got better, not worse:** the insert now writes only columns that exist
at head, and the `'Local'`/NULL values come from production's own restored DEFAULT rather than a
hand-written literal.

**§12's security clause is satisfied:** security-critical change **with** tests and a
`security-auditor` APPROVE (0 Blocker / 0 Major, against the final diff) rides the normal
automerge flow; Klas reviews post-merge.

**Minor 1 and Minor 2 stand unchanged and are not re-graded.** The session's disposal of both is
confirmed explicitly: no procedure written, `release-checklist.md` untouched, and the measurement
not run against the shared Postgres surface. **Minor 2's measurement is not "skipped" — it is
scheduled to `bootstrap`,** and must be written that way in the PR body; a parked step is never
phrased as a completed one.

## Escalation to Klas — verbatim, unchanged between rounds

> **Identity-kontexten (`AppIdentityDbContext`) har ingen automatisk appliceringsväg i den levererade deploy-stacken.** `migrate`-tjänsten kör `command: ["schema"]` (`deploy/docker-compose.yml:493`), och `schema` är `AppDbContext only` (`src/Jobbliggaren.Migrate/Program.cs:334`). Identity-migrationer appliceras enbart av `docker compose run --rm migrate bootstrap` med master-creds — uttryckligen en operatörsåtgärd (`deploy/docker-compose.yml:488`). `docs/runbooks/release-checklist.md` rad 43-45 äger det och säger *"Identity-schema-ändring → manuell procedur (parkerad, #1172)"*. **Triggern har nu fyrat:** #1747 är den första Identity-schema-ändringen sedan lådan provisionerades, och proceduren är fortfarande parkerad.
>
> Två frågor, och de kräver olika svar:
>
> **1. När, och av vem, ska `20260917195454_DropAuthProviderColumns` appliceras på lådan?** Tills någon kör `bootstrap` för hand säger repot, ADR 0017:s amendment och Art. 30-registret att kolumnerna är borta medan `identity."AspNetUsers"` fortfarande bär dem. **För 6d är det ofarligt:** båda kolumnerna är tomma (`provider = 'Local'`, `provider_user_id` NULL), ingen kategori, rättslig grund, retention eller mottagare ändras, och registrering fortsätter fungera eftersom `provider` har DB-default `'Local'` (`20260506160036_AddAuthProviderToUser.cs:22`). **Detta är alltså ingen GDPR-Blocker och inget skäl att hålla PR:en.**
>
> **2. Ska den manuella proceduren skrivas innan del 5b, eller får 5b landa på samma parkerade rad?** Det är den fråga jag vill ha svar på nu, medan den är billig. **5b är en Identity-migration till** — ADR 0142: *"`password_hash` nulled, `security_stamp` rotated in the same statement"* — och den bär **autentiseringsuppgifter**, inte tomma kolumner. Samma tysta gap där betyder att ADR, checklista och register säger att lösenordshasharna är nollade medan lådan har dem kvar: en Blocker-formad utgång som ingen grind i stacken kan se, eftersom `#1236`-schema-ahead-grinden bara läser `AppDbContext`.
>
> **Jag graderar detta Minor på #1747 och inte Major**, eftersom 6d:s egen effekt är ofarlig och en blockering här flyttar in en deadlock i en människa för tillstånd PR:en inte orsakat. Men 5b-frågan är din, inte CC:s: att köra `bootstrap` med master-creds på produktionslådan är en deploy-åtgärd (CLAUDE.md §9.2), och proceduren ligger parkerad i #1172.
