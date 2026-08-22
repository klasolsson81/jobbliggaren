# CTO bind — the orphan model (#1349 + #1409)

- **Agent:** `senior-cto-advisor` (decision-maker, §9.2)
- **Date:** 2026-08-22
- **Worktree:** `c:/tmp/jbl-orphan`, branch `fix/orphan-verify-email-1349`, HEAD `751924d5` (= `origin/main`)
- **Scope:** the shared design question behind #1349 and #1409 — what an orphan is, and who repairs it.
- **Everything below was measured in this worktree today.** Where I overturn a prior position, the
  measurement that overturns it is named.
- ⚠ `docs/reviews/` is gitignored (`.gitignore:158`). If Klas must read this on GitHub, promote it
  with `git add -f` (§9.2's `.gitignore` exception).

---

## Measurements that decide the bind

| # | Measurement | Where |
|---|---|---|
| M1 | `/verify-email` returns **204 No Content**. It attests one fact: *this address is confirmed*. It carries no account-existence claim. | `AuthEndpoints.cs:217-226`, `VerifyEmailCommandHandler.cs:22-27` |
| M2 | **No domain table holds an email or a username.** Every `email` column lives in the Identity schema; `AuditLogEntry` carries `Guid? UserId` and no address. | `git grep HasColumnName..email src` → Identity migrations only; `AuditLogEntry.cs:14-29`; `JobSeeker.cs:10-60` |
| M3 | `HardDeleteAccountAsync` is **already correct for a reverse orphan**: it loads the JobSeeker with `IgnoreQueryFilters()` and no `DeletedAt` precondition, and step 2h's `FindByIdAsync` returns null → no-op. Only the *trigger* is missing. | `AccountHardDeleter.cs:149-160`, `:326-328` |
| M4 | Producer 3 is **dormant, not live**. After #1117 moved `ValidateDisplayName` before `CreateUserAsync`, `JobSeeker.Register`'s only remaining failure trigger is an empty `userId`, which the real Identity adapter never returns. Two tracked docstrings now contradict each other. | `RegisterCommandHandler.cs:50-52` vs `:113-118`; `RegisterCommandHandlerTests.cs:369-374` vs `OrphanedIdentityActivationTests.cs:196-198` |
| M5 | The forward orphan **self-heals by deletion within ≤25 h**, which frees the email (UNIQUE in Identity), so the user can simply register again. | `AccountHardDeleter.cs:84-88`, `:103-114`; `HardDeleteAccountsJob.cs:41-44`, `Cron.Daily(4)` |
| M6 | The confirmation flow over-claims in **four** copy keys, in **two** locales, and one page test asserts the literal string. | `messages/{sv,en}/pages.json:499-509`; `bekrafta-konto/page.test.tsx:96` |

I could not read `docs/reviews/2026-08-22-mvp-prio-cto.md` — gitignored, not synced into this worktree.
Its quoted sentence is taken from the driving session's report and is treated as the source.

---

## BESLUT 1 — the orphan model

### An account is the pair. An orphan is never completed; it is always erased.

An **account** is the pair (`ApplicationUser`, `JobSeeker`). Neither half alone is an account — this is
already delivered doctrine (`LoginCommandHandler.cs:72-88` guards the capability seam) and I ratify it.
An **orphan** is a surviving half. **There is exactly one disposition, in both directions: erasure.**
Only the trigger differs.

|  | Forward (Identity, no JobSeeker) | Reverse (JobSeeker, no Identity) |
|---|---|---|
| Carries | a credential; **no user content, no DEK** | **all user content + DEKs**; no email, no username (M2) |
| Data subject reachable | yes — the address is right there | **no** |
| Completable | no — no name exists to provision with | **no** — nobody can be proven to own it |
| Disposition | delete the credential | delete the profile + cascade |
| Repairer | **a job** — `CleanupIdentityOrphansAsync`, delivered | **an operator** — no trigger exists |
| Never the repairer | the user | the user |

### GRUND

**Neither half is completable, and that is the load-bearing half of the answer.**

*Forward.* Completion needs a `DisplayName` — a fact **about a person, stated by that person**. It
existed only in the rolled-back request. Any substitute (email local-part, placeholder, blank via a new
domain method) is a datum we invented and attributed to a data subject: §5's *"synthesising prose the
user did not write"* states the rule, GDPR Art. 5(1)(d) states it as law. It would also require opening
`JobSeeker`'s constructor invariant (`ValidateDisplayName` rejects blank) — paying for an infrastructure
failure path with a permanent hole in an aggregate invariant (Evans 2003, "Aggregates"; Martin 2017
kap. 9, OCP).

*Reverse.* Completion needs to prove a person owns the data. Per **M2** the only identifier that ever
tied a person to that row died with the Identity row. The single remaining trace sits inside the
DEK-encrypted CV content — so re-identification means **decrypting the data subject's CV in order to
find out whose CV it is**: a remedy that consumes the interest it claims to protect (Art. 5(1)(b)
purpose limitation), and unreliable besides (not every user uploads a CV). **Re-linking is not
deferred; it is unsound.** #1409's *"Not proposed here"* names *åter-länkning* as a candidate remedy —
that arm should be **struck, not scheduled**.

**The user's own remedy is to register again** (M5). The product's promise is *"a registration that did
not complete leaves nothing behind"* — not *"we will finish it for you"*. That promise is already true
in code. It is simply never said to the user, which is #1349's whole remaining defect.

**The asymmetry is correct, and its ground has never been written down.** Given that registration must
write two boundaries in some order, the half that survives a partial failure should be the one carrying
**no personal data** and erasable **without identifying anybody**. That is the credential half. This is
Art. 25(1) data protection by design applied to failure modes, plus Art. 5(1)(c).

### AVVISADE ALTERNATIV

- **Lazy provisioning at confirmation (issue's own alternative; the driving session's measurement B).**
  Rejected — B is decisive, not merely costly. There is no name, and inventing one is the §5 /
  Art. 5(1)(d) breach above. B **disqualifies** the path.
- **Store `DisplayName` on `ApplicationUser` so provisioning becomes a pure function of the Identity row.**
  Rejected: a bounded-context leak (Evans 2003) — `ApplicationUser` is the credential context; a second
  home for one knowledge piece with a permanent sync obligation on `UpdateDisplayName` (DRY, Hunt/Thomas
  1999); and it puts a name into the Identity schema, **outside the DEK envelope**, widening the
  crypto-erasure gap to buy a recovery path we have just decided not to want.
- **Make registration atomic across the two boundaries.** Standing prohibition, `AccountHardDeleter.cs:74-83`.

---

## BESLUT 2 — #1349's PR scope

### IN (a): the screen stops lying — by deletion of the claim, in copy only

**GRUND.** Per **M1** the API does not lie. `/verify-email` returns 204 and attests exactly *"this
address is confirmed"*. The FE invented an account-existence claim on top of a contract that never made
one. The defect is precisely located in `messages/{sv,en}/pages.json`, and the fix is to **strike the
unwarranted claim and keep the warranted one** — §9.6's closing discipline (*a fix deletes text; it
never adds a claim-sentence*) applied at product level. It needs no endpoint change, no new state, no
oracle, no new test premise, and it is true in **both** worlds.

**What is binding is the property, not my wording:** *no key in this flow may assert that an account
exists, or that login will succeed.* Exact prose is `jobbpilot-design-copy` + `design-reviewer`'s call.

Proposed (sv; `en` in parallel — `messages/{sv,en}.json` is a §6.5 **hotspot**, confirm ownership):

| key | from | to |
|---|---|---|
| `title` | Aktivera ditt konto | Bekräfta din e-postadress |
| `intro` | …bekräfta din e-postadress och aktivera kontot. Sedan kan du logga in. | Klicka på knappen för att bekräfta din e-postadress. |
| `confirm` | Aktivera kontot | Bekräfta e-postadressen |
| `successTitle` | Kontot är aktiverat | Din e-postadress är bekräftad |
| `successBody` | Din e-postadress är bekräftad. Logga in för att komma igång. | Logga in för att komma igång. |

**All four keys, not just `successTitle`** — a partial fix leaves the button saying *"Aktivera kontot"*
and the result saying *"adressen är bekräftad"*, which is incoherent and lands the fix in one of N
places. `bekrafta-konto/page.test.tsx:96` asserts the literal old string and changes with it (M6).

### IN (b): correct the stale producer enumeration

**GRUND.** `OrphanedIdentityActivationTests.cs:196-198` lists producer 3 (the compensating
`DeleteUserAsync`) as live; `RegisterCommandHandlerTests.cs:369-374` records in the same repo that its
only remaining trigger is an empty `userId` the real adapter never returns (**M4**). Two tracked
docstrings contradict each other about live behaviour — §5 `Comments:`, *"a factually wrong comment …
is a defect and is fixed"*. In-block: same file, same subject, and the enumeration **is** this PR's
subject. The correction **narrows** a claim; it adds none.

The honest post-PR statement is not *"four producers remain"* but: **one live silent producer** (a
cancelled request), **one live self-compensating producer** (step 2h, with a written compensating path),
**one dormant arm**, **one historical population**.

### IN (c): record why the order is correct — one sentence

**GRUND.** See Beslut 4. The direction of the asymmetry is a real decision that exists only implicitly,
which is what let a later reading call it a bug. One sentence in `AccountHardDeleter`'s class docstring
beside the existing two-boundary rationale (`:19-28`), naming the Art. 25(1) / 5(1)(c) ground. This is
not prose added to close a finding — it records a decision — so §9.6's no-new-claim rule does not reach
it. Keep it to one sentence. An ADR 0024 D6 amendment is the canonical home and is `adr-keeper`'s call;
**recommended, not blocking.**

### OUT (d): do not differentiate `/verify-email`'s response — named skip

**GRUND.** I considered it seriously. It is **not** an enumeration oracle — the success branch is
reachable only with a token minted to that inbox — so the usual ground does not apply. It fails on three
others: it injects an account-completeness concept into a command whose single responsibility is address
confirmation (SRP, Martin 2017 kap. 7); it makes a **public unauthenticated endpoint** read domain state
it has no other reason to read, for zero user benefit (the holder can act on it no differently); and it
closes a finding by **adding** a claim. The 2026-08-19 bind stands and extends: `/verify-email` must
neither refuse **nor report**.

### OUT (e): do not make producer 1 visible — named skip

**GRUND.** `UnitOfWorkBehavior` is generic over every command; a cancelled request is ordinary there (a
user closes a tab) and it cannot distinguish a two-boundary half-commit from a clean single-boundary
rollback. Logging there is noise **and** an SRP break — its one reason to change is unit-of-work policy,
not registration's hazard (Martin 2017 kap. 7, kap. 11). Logging in `RegisterCommandHandler` is
impossible without restructuring who owns `SaveChangesAsync`: the handler returns *before* the save.
Buying one observability line with a pipeline-contract change is the wrong trade. And it buys little —
the **population** is already reported (`LogOrphansCleaned` each run, EventId 2504 per failed
sweep-delete, runbook §3.3 on demand). What is missing is *cause attribution*, which an operator infers
from timing.

### OUT (f): do not touch step 2h's discarded `IdentityResult` (`AccountHardDeleter.cs:326-328`)

**GRUND.** #1410 distinguished it deliberately and wrote the ground at `:121-124` — it carries a
compensating path (step 0, next run) where the other two carried nothing. Named here so nobody
"completes the set."

### OUT (g): no recovery guidance on the login-failure surface

Static state-independent copy there is not an oracle and may well be defensible, but it is #714/#1272's
surface and a **different change-reason** (§6, one concern per PR).

### Observation, routed nowhere

`invalidBody` says *"Registrera dig igen för att få en ny länk"*, which is false when
`RegistrationsOpen=false` (the code default) — but **true in the deployed configuration**, where
registration is open. Not a defect today. Do not touch it; naming it so a reviewer who spots it in the
same key group is answered in the PR body.

---

## BESLUT 3 — #1409's PR scope

### Klas's scoping is CONFIRMED. Do not widen. But the procedure's SHAPE is bound, because the obvious shape is wrong.

**Do NOT write a hand-rolled SQL erasure cascade into the runbook.** `HardDeleteAccountAsync` erases
eleven aggregates plus the DEKs plus audit anonymisation, and the completeness of that list is
machine-checked by `AccountHardDeleteCascadeFitnessTests` (its iff-invariant, every arm). A SQL
transcript in a runbook is **a second home for that knowledge with no guard on it** — DRY in its real
sense (Hunt/Thomas 1999: one home per knowledge piece, not per lookalike text) and CCP (Martin 2017
kap. 13). The next aggregate added updates the code and the fitness test and **silently leaves the
runbook erasing ten of twelve tables** — a GDPR under-erasure defect manufactured by a document.

**The procedure re-enters the guarded path instead.** Per **M3** the erasure path is already correct for
a reverse orphan; only the trigger is missing. One statement supplies it:

```sql
UPDATE job_seekers SET deleted_at = NOW() WHERE id = '<jobSeekerId>'::uuid;
```

`GetAccountsReadyForHardDeleteAsync` then selects it and `HardDeleteAccountsJob` performs the full,
tested cascade. Two variants, both stated:

- **No Art. 17 request (Art. 5(1)(e) housekeeping):** `NOW()`. The 30-day window costs nothing here —
  nothing is restorable anyway — and buys a **free operator undo** for a mis-identified row. Keep it.
- **A verified Art. 17 request:** backdate past the cutoff (`NOW() - INTERVAL '31 days'`) so erasure
  runs on the next pass (Art. 17(1), *without undue delay*). The runbook **must** require the real date
  in the ops channel — the backdated `deleted_at` is an operational trigger, not a true fact about when
  erasure was requested. §4 already sets this precedent (*"Logga manuellt i ops-channel"*).

Also in the section: §3.3's query is the identification step (already present); **state that no Redis
tombstone cleanup applies** — a reverse orphan has no Identity row so no session can exist, and an
operator reading §4 by analogy would otherwise hunt for one; and a verification SELECT after the run.

### Do not widen to the `P3` technical remedy

**GRUND (§6.5).** No real test user meets it — zero measured instances, and the population needs an
operator action or an app clock over an hour ahead of the database, which ADR 0024 itself calls gross
misconfiguration. And it does not block going live **once the procedure exists**: the procedure is
exactly what converts *"blocks go-live"* into *"handled"*. Record on the issue that if it is ever built,
Beslut 1 has already narrowed its design space to one arm — scheduled erasure; **re-linking is out**.

### Filing discipline

#1409's body says *"the absence of `mvp` … are the CTO's routing"*. The label set now carries `mvp`
(Klas, 2026-08-19). The body has decayed relative to its own labels — edit that line when working the
issue. A claim with no date cannot be told from a claim that has decayed.

---

## BESLUT 4 — the sequence

### (A) is right in its conclusion and understated in its reason. And the order is not the bug.

**The order must not be reversed at all — deletion path or no.** Reversal converts a content-free,
contactable, **self-healing** orphan (M5) into a content-bearing, **uncontactable** (M2), trigger-less
one. You do not trade the cheap failure mode for the expensive one to buy symmetry. So the answer to
(A) is stronger than *"not until #1409 has a deletion path"*: not ever.

**Therefore there is no hard sequence between the two PRs**, because the change that would have created
one is a change I am forbidding. They are a **thematic pair sharing one model** — which is what the
2026-08-22 prioritisation actually needs them to be.

**#1349 still goes first.** It carries the live user-visible defect; it is `P2` against `P3`; it gates
#183's mail-prod-flip, since a working provider makes the registration path reachable by real users and
the false screen is what they meet; and #1409's MVP slice is a runbook edit that depends on nothing in
#1349.

**Overturned: *"buggen är ORDNINGEN"* (CTO comment 2026-08-22).** On measurement the order is not the
bug — **it is the mitigation**. What was missing is that nobody wrote down why it is the correct order,
and that absence is what let a later reading call it a defect. Beslut 2(c) closes it in one sentence.

---

## BESLUT 5 — what must NOT be done

1. **Never provision a `JobSeeker` outside a registration the user completed** — not at `/verify-email`,
   not at login, not at resend, not in a job. (Beslut 1.)
2. **Never repair from the duplicate-registration branch** (`RegisterCommandHandler.cs:72-106`) — the
   driving session's measurement (C). **This is the strongest reject in the bind, and it looks like the
   elegant answer, which is why it is written loudly.** That branch is reachable by **anyone** who
   submits an address, a password and a display name: no token, no session, no proof of inbox control.
   Provisioning there lets an unauthenticated stranger write an **attacker-chosen `DisplayName`** onto
   another person's account and thereby make that account loginable. That is an unauthenticated write of
   personal data attributed to a data subject on a third party's say-so — GDPR Art. 5(1)(d) and Art. 32,
   and a §5 `Security:` class. **It is not a seam. It is a hole.** Answering (C): neither. Reject
   unconditionally.
3. **Never reverse the two-boundary write order.** (Beslut 4.)
4. **Never introduce a cross-context transaction.** Standing, `AccountHardDeleter.cs:74-83`.
5. **Never put `DisplayName` or other domain PII on `ApplicationUser`.** (Beslut 1, rejected alternatives.)
6. **Never differentiate `/verify-email`'s response** on profile presence — refuse *or* report.
7. **Never make `/resend-confirmation` existence-dependent.** Ratified; restated so it is not reopened.
8. **Never change login's uniform 401.** Ratified (`security-auditor` STEG 10b Major-1). It *is*
   misleading about the cause; that is the knowingly-paid price of closing the account-status oracle.
9. **Never hand-roll the erasure cascade in SQL.** (Beslut 3.)
10. **Never add `email_confirmed` to the sweep predicate.** Measured correct, and Beslut 1 says why:
    confirming an empty credential does not make it non-empty, so deletion stays right.
11. **Never file a TD.** The register is retired (#1172).

---

## Accepted remainder — written as a remainder, not omitted

**R1 — the forward orphan's dead end survives this PR, for up to ~25 h.** After Beslut 2 the screen no
longer lies (*"din e-postadress är bekräftad"* is true), but a user who then tries to log in still meets
*"E-post eller lösenord är felaktigt"*, which is misleading about the cause. That is the ratified price
of the uniform 401 (item 8 above) and I am not reopening it. Their real remedy — register again once the
sweep has run — is communicated nowhere.

**Disposal:** carry R1 in the PR body **and** as a comment on #1349 before closing. It is not a new
defect in delivered code; it is the named consequence of a ratified trade-off, so it belongs with the
decision rather than in a new backlog row. #1349's comment thread is where the next reader looks.
**Grade belongs to whoever reviews the PR — I route, I do not grade** (§9.6).

---

## Issues to file

**None.** Net effect on the backlog: **0 filed**, #1349 closes, #1409's MVP slice closes (`P3` remainder
stays open, re-scoped per Beslut 3). §9.6's net cap is satisfied with room.

---

## Referenser

- Robert C. Martin, *Clean Architecture* (2017) — kap. 7 (SRP), kap. 9 (OCP), kap. 11 (DIP), kap. 13 (Component Cohesion: REP/CCP/CRP)
- Eric Evans, *Domain-Driven Design* (2003) — "Aggregates", "Bounded Contexts"
- Hunt/Thomas, *The Pragmatic Programmer* (1999), kap. 7 — DRY as one home per knowledge piece
- Microsoft Learn — *Architect modern web applications with ASP.NET Core and Azure*: Architectural principles (SoC, DIP)
- GDPR Art. 5(1)(b)(c)(d)(e), Art. 17(1), Art. 25(1), Art. 32
- CLAUDE.md §9.6 (where a finding goes; closing discipline), §6.5 (`mvp` criterion, hotspots), §6 (one concern per PR); AGENTS.md §5 (`Comments:`, `Security:`), §1.6
- ADR 0024 D5/D6 (two-boundary registration, restore window, orphan sweep); #508, #714, #1117, #1172, #1369, #1410

---

# Tillägg 2026-08-22 — scope-fråga på Beslut 2(a): HEM B (bekräftelsemejlet)

Den drivande sessionen körde **regeln jag band, inte min uppräkning**, och svepet hittade ett hem M6
inte nådde. Det är rätt läsning av bindet, och M6 var ofullständig. Mätt av mig i samma arbetsträd,
`751924d5`.

## Svar 1 — HEM B är **IN-BLOCK** i #1349

**GRUND.** Change-reason för HEM A och HEM B är **samma mening**: *bekräftelseflödet får inte påstå
att ett konto finns eller att inloggningen kommer att gå.* Samma flöde, samma population, samma
PR-ämne. §9.6 ger följd-PR endast åt ett **genuint eget** change-reason; det här är ett och samma
kunskapsstycke i ett andra hem (DRY, Hunt/Thomas 1999). Att dela det vore att institutionalisera
"en fix på ett hem av två". Scope-storlek är inget kriterium (Regel 3).

**Egenskapen räcker på egen hand här — min (a)-grund gör det inte, och den drivande sessionen har
rätt i att den inte överförs.** (a) vilade på en *diskrepans mellan svar och rendering* (204:an
intygade bara adressen); mejlet har inget svar att avvika från. Men båda strängarna fäller
egenskapen direkt, var för sig:

- **"Tack för att du har skapat ett konto på Jobbliggaren."** — påstår rakt av **att ett konto
  finns**. Under Beslut 1 är ett konto *paret*, så för en forward orphan är meningen falsk. Skarpare
  än så: vid sändningstillfället (`RegisterCommandHandler.cs:142`) har `db.JobSeekers.Add` körts men
  `SaveChangesAsync` **inte** — den ligger i `UnitOfWorkBehavior` efter handlern. Meningen påstår
  alltså ett fullbordat tillstånd som vid utsagan är ocommittat **för alla**. Den blir sann några
  millisekunder senare i normalfallet, och aldrig för producent 1.
- **"Du kan logga in när adressen är bekräftad."** — motargumentet (villkor, inte tillstånd) är
  seriöst men faller på läsningen. Svenskans *"du kan logga in när X"* utsäger att X är
  **tillräckligt**. För en orphan är det inte tillräckligt — bekräftelsen ger ingenting, vilket är
  hela #1369-vakten. Alltså: påstår **att inloggningen kommer att gå**. Docstringen läser den som
  *nödvändig* ("cannot log in until"); hade vi menat nödvändighet hade meningen lytt "Du behöver
  bekräfta adressen innan du kan logga in". Den naturliga läsningen är en utfästelse.

**Omfattning, mätt:** `EmailTemplates.cs` — "skapat ett konto" i **två** renderingar (`:499` text,
`:514` html), "Du kan logga in när…" i **tre** (`:501-502` text, `:513` preheader, `:517-518` html).
Mejlen är hårdkodad svenska, ingen `en`-parallell.

**Föreslagen åtgärd = strykning, inget tillägg** (§9.6:s fix-form). Ta bort login-meningen helt —
omgivningen bär redan allt sant som behövs ("Bekräfta att adressen är din genom att öppna länken
nedan. Länken gäller i 24 timmar."). Preheadern blir "Länken gäller i 24 timmar." Första meningen →
*"Tack för att du har registrerat dig på Jobbliggaren."* (beskriver **användarens handling**, som är
sann, i stället för vårt tillstånd). Binder egenskapen, inte orden.

⚠ **Två fällor vid redigeringen.** (1) Text- och HTML-renderingen hålls ordagrant lika **för hand**
— kommentaren `:519-521` säger det och **inget test pinnar det**. Alla tre renderingarna ändras
tillsammans eller ingen. (2) `EmailTemplatesEmailConfirmationTests` pinnar länkform, subject,
`"24 timmar"` och civic-tone-negativen — **inte** de två meningarna. Strykningen bryter alltså inget
test, vilket också betyder att **inget test fångar en halv strykning**.

## Svar 2 — `successBody` stannar, avsiktligt (men den var oprövad tills nu)

Rakt svar: den **föll inte mellan** egenskapen och tabellen av ett skäl jag hade formulerat — den
överlevde för att jag läste den som uppmaning utan att pröva den. Nu prövad, och den stannar.

**Gränsen, utskriven, eftersom den drivande sessionen just gick den:** egenskapen förbjuder att
**förutsäga utfallet** av en handling, inte att **erbjuda nästa handling**. *"Logga in för att komma
igång."* pekar på det enda vettiga steget vid en punkt där användaren redan gjort allt hen kan; tar
man bort den blir skärmen död, och en CTA presupponerar utfall i exakt samma svaga mening som varje
inloggningsknapp i världen — nådde egenskapen dit skulle den förbjuda själva login-länken. *"Du kan
logga in när adressen är bekräftad"* erbjuder ingen handling: den namnger ett villkor och garanterar
utfallet av att uppfylla det. Det är skillnaden, och den är egenskapens egen.

R1 bär redan resten: inloggningsförsöket blir vilseledande för en orphan. Att behålla CTA:n och
acceptera R1 är koherent — att stryka den vore sämre för alla och skulle inte laga R1 ändå.

## Svar 3 — samma egenskap; docstringen ändras men på annan grund

**Samma egenskap**, och den är självbärande i HEM B (se ovan). Det som **inte** överförs är min
(a)-*grund* — korrekt sett av den drivande sessionen.

**Docstringen (`:478-479`) räknas inte in i egenskapen** — den är inte user-facing, och egenskapen
handlar om vad vi säger till användare. Den ändras ändå, på **§5 `Comments:`**-grund: när de tre
renderingarna stryks beskriver *"The account cannot log in until the link is opened"* ett beteende
koden inte längre uttrycker, och den är dessutom bäraren av just den nödvändighetsläsning vi
överger. En faktiskt felaktig kommentar är en defekt och lagas. Två grunder, en åtgärd.

## HEM C — som jag hittade i mitt eget svep: **följd-PR, inte skip, inte in-block**

`AccountExistsNotice` (`EmailTemplates.cs:542-585`) fäller samma egenskap tre gånger: subject *"Du
har redan ett konto hos Jobbliggaren"*, brödtextens *"du har redan ett konto"*, och *"Om det var du
kan du logga in i stället:"*. En orphan-innehavare som registrerar om sig får **precis det mejlet** —
#1349:s egen reproduktion går genom det steget.

**Ändå eget change-reason, och det är inte en scope-nedskärning.** HEM B är en strykning. HEM C är en
omskrivning av det mejl vars **hela säkerhetsfunktion** är att vara enda differentiatorn mellan
tagen och fri adress (dess egen docstring, #714) — och det måste bli sant **utan att bli
tillståndsberoende**, för annars vandrar vi rakt in i förbud 2. Det är en designfråga, inte en
strängändring, och `security-auditor` måste se den.

Och den är **substantiell, inte kosmetisk**: HEM C är den naturliga hemvisten för R1:s saknade
halva. Det är mejlet användaren får när hen gör exakt rätt sak (registrerar om sig), så följd-PR:ns
change-reason är *"account-exists-notisen säger sanning och bär återhämtningsvägen, utan att bli
tillståndsberoende"*. Den stänger både HEM C:s tre påståenden och R1:s kommunikationsglapp.

**Villkor:** följd-PR:n skapas i samma session direkt efter att #1349 mergat — inte "någon gång" —
och #1349:s PR-body namnger den. Jag designar den inte här; det är den PR:ens arbete.

## Netto

Backloggen oförändrad: **0 issues filade**. #1349 bär HEM A + HEM B (+ docstringen). HEM C blir
följd-PR nummer tre i lanen; #1409:s runbook-skiva är liten nog att rymma det.

---

# Tillägg 2026-08-22 (2) — HEM C + HEM D: designbindet för PR 3

Mätt av mig på `origin/main` `6a03fee3` (HEM B landade i #1438: *"Tack för att du har registrerat dig"*,
och `"Du kan logga in när"` grepar till noll). Två av mina egna påståenden faller nedan.

## Den generella regeln, som ersätter min uppräkning

**Varje yta får påstå exakt vad dess egen trigger fastställer, och inget mer.** Skärmen: 204:an
fastställer *adressen bekräftad*. Registreringsmejlet: att användaren registrerade sig. Duplicate-grenen:
att **adressen är tagen i Identity** — inte att ett konto finns. Login-grinden: att `EmailConfirmed=false`
— inte att bekräftelse räcker. Fixen är i varje hem att **krympa påståendet till triggern**, aldrig att
lägga till en gren.

**Och: att låta LÄSAREN förgrena är inte att låta KODEN förgrena.** En självvald villkorssats
(*"Kommer du inte in?"*) håller mejlet byte-identiskt för varje mottagare — förbud 2 orört — och är
verksam bara för den population den namnger. Det är den tredje formen, och den är mekanismen för
både Q1 och Q2.

## BESLUT 1 (Q1) — formen håller. Ramsatsen ryker, knappen stannar.

**GRUND.** *"Adressen är redan registrerad"* är exakt vad `CreateUserAsync`-felet
(`DuplicateAccount`) fastställer. *"Du har redan ett konto"* är mer än så, och falskt för en forward
orphan under Beslut 1:s par-modell. Formen läser **inget** domäntillstånd — den säger vad grenen redan
vet. Ingen fälla jag kan mäta: mejlet når bara adressens egen inkorg, så innehållet påverkar inte
202-uniformiteten (#714 rör inte innehåll, bara status/kropp på HTTP-svaret).

**Den enda fällan jag ser är strukturell, och den är värd att binda som en checkbar regel:**
`AccountExistsNotice(string baseUrl)` tar **bara `baseUrl`** — den har ingen åtkomst till kontotillstånd
alls, inte ens ett userId. Tillståndsberoende är alltså **strukturellt omöjligt i mallen i dag**. Risken
ligger inte i copyn utan i att någon senare **växer signaturen**. Bind det så: *mallens signatur får inte
få en tillståndsparameter, och `EmailNotConfirmedMessage` får inte sluta vara en `const`.* Det är
greppbart; "var inte tillståndsberoende" är det inte.

**Knappen:** din skärpning är rätt om ramsatsen och fel om knappen. Mejlet *vet* att adressen är tagen
— men det det vet är att den är tagen **i Identity**, vilket är precis inte *"du kan logga in"*.
*"Om det var du kan du logga in i stället:"* påstår alltså mer än triggern fastställer och **ryker**.
`EmailHtml.Button(loginLink, "Logga in")` erbjuder en handling och påstår ingenting — den faller under
Svar 2-gränsen och **stannar**. Ersätt ramsatsen med en självvald villkorssats, inte med en ny utsaga.

**Tre påståenden — men NIO renderingar, och du listade fyra.** Mätt i `EmailTemplates.cs` (identisk med
`origin/main`, diff = 0):

| Påstående | Renderingar |
|---|---|
| *"(Du har) redan ett konto"* | subject `:548`, text `:550-551`, **html `title:` `:567`**, html P `:569-571` |
| *"kan du logga in i stället"* | text `:553`, html `:572` |
| *"Ditt konto är oförändrat"* | text `:559-560`, preheader `:568`, html P `:578` |

⚠ **`title:` på `:567` saknas i din uppräkning** — samma "ett hem av N"-fälla en tredje gång. Text och
HTML hålls ordagrant lika **för hand** och **inget test pinnar pariteten**, så ingenting fångar en halv
strykning. *"Ditt konto är oförändrat"* bär en **försäkran** åt en verklig ägare; krymp den
(*"Ingenting har ändrats"*), försvaga den inte.

⚠ `security-auditor` berömde i #1438 att mejlets subject var **oförändrat**. PR 3 ändrar det legitimt
(innehåll når bara ägaren) — men räkna med att hon mäter om det.

## BESLUT 2 (Q2) — tredje formen finns, men R1 är smalare än jag skrev

**Jag hade fel, och det är mätt av `security-auditor` i `docs/reviews/2026-08-22-orphan-security-auditor.md:54-56`.**
Jag skrev att botemedlet är *"communicated nowhere"*. Falskt: efter svepet ger länken uniform 400, FE
kollapsar varje 4xx till `invalidBody`, och den strängen säger redan **"Registrera dig igen för att få en
ny länk."** Botemedlet ÄR kommunicerat — på bekräftelselänksytan, efter svepet.

**R1:s verkliga rest är därför bara fönstret mellan bekräftelse och svep, och bara på login-ytan.**
Där landar orphanen på den uniforma 401:an, som är ratificerad och orörbar. **R1 kan alltså inte
stängas helt här, och det är en hård begränsning, inte ett scope-val.**

**BESLUT: bär den tredje formen i HEM C, inte i HEM D.** I HEM C: en självvald villkorssats som leder
till `ContactAddress`, **som redan står i mejlet** — omramning, inte tillägg. En människa kan slå upp
raden och lösa det direkt; *"registrera dig igen om ~25 h"* avvisas nedan.

**Ingen issue.** `security-auditor`s fallback (`:60-61`) är villkorad: *"skapas HEM C inte innan
sessionen slutar → fila R1 som issue"*. HEM C skapas nu, så villkoret är uppfyllt — men **i sak, inte
bara i bokstav**: PR:en måste faktiskt bära villkorssatsen, annars är villkoret kringgått. PR-bodyn
skriver ut att R1 är **delvis** stängd och var resten bor, så ingen senare läser HEM C som en full
stängning.

## BESLUT 3 (Q3) — HEM D **IN** i PR 3. Ett change-reason, två hem.

**Jag mis-scopade OUT (g), och det är mitt fel att rätta.** Jag skrev "login-ytan, eget
change-reason" — men jag avböjde där att **lägga till** återhämtningsvägledning. HEM D är att **ta
bort** ett falskt tillräcklighetspåstående: samma change-reason som HEM A och HEM B, tredje renderingen
av samma kunskapsstycke. §9.6 nycklar på change-reason, inte på plats. OUT (g) täcker inte HEM D.

**Och HEM D är den dominerande populationen, inte en kantfall.** Mätt: `ValidateCredentialsAsync`
returnerar `EmailNotConfirmed` som **sista** grind (`UserAccountService.cs:149-151`), och
`LoginCommandHandler.cs:23-38` returnerar på `IsFailure` **före** JobSeeker-vakten `:72-88`. Producent 1
(avbruten request) och producent 4 (historiska rader) producerar **båda** *obekräftade* orphans — så
HEM D är den första ytan de möter, och den enda före den orörbara 401:an.

⚠ **HEM D är TVÅ hem, inte ett — och FE:t kastar API:ts mening.** `actions.ts:100-108` renderar
`t("auth.actions.emailNotConfirmed")`, **inte** `detail` från svaret. Men `AuthEndpoints.cs:411-414`
lägger `AuthErrorCodes.EmailNotConfirmedMessage` på wire som ProblemDetails `detail`. Alltså:
`messages/{sv,en}/pages.json:482` = vad användaren ser; `AuthErrorCodes.cs:76-77` = vad wire bär. **Två
hem, en mening, ingen paritetspinne.** Ändra båda, annars är det en fix på ett hem av två.

**Åtgärd = ren strykning av tillräckligheten.** *"Bekräfta din e-postadress **för att logga in**"* →
ett konstaterande av vad grinden fastställer, t.ex. *"Din e-postadress är inte bekräftad än."*
Resend-knappen (`LoginForm.tsx:93`) står kvar — den är rätt för den legitima obekräftade användaren.

**Ingen återhämtningssats i HEM D**, och det är avsiktligt: användaren där har en korrekt nästa handling
(bekräfta). Att i förväg säga "och funkar inte det, hör av dig" är brus för 99,99 % och för tidigt för
orphanen, som ännu inte gått i väggen. Väggen är den uniforma 401:an.

**Andra meningen, `"Vi har skickat en länk till din inkorg"`:** utanför den bundna egenskapen, men den
faller på den generella regeln — grinden fastställer `EmailConfirmed=false` och vet ingenting om
sändningar (som sedan #1369 sväljs vid fel). Gör den till **instruktion i stället för påstående**:
*"Kontrollera din inkorg."* Samma Svar 2-gräns, konsekvent tillämpad.

## BESLUT 4 (Q4) — vad som INTE ska göras

1. **Väx inte `AccountExistsNotice`s signatur** med en tillståndsparameter, och gör inte
   `EmailNotConfirmedMessage` till något annat än en `const`. Det är den checkbara formen av förbud 2.
2. **Förgrena inte texten på profilnärvaro** — varken i mejlet eller i meddelandet. Läsaren förgrenar; koden inte.
3. **Skriv inte ut svepet.** *"Registrera dig igen om cirka ett dygn"* avvisas: det exponerar intern
   maskineri, det blir fel om grace-fönstret eller cron-tiden ändras, och det är främmande copy för en
   civic utility. `ContactAddress` routar till en människa som kan lösa det nu.
4. **Rör inte den uniforma 401:an** (ratificerad, `security-auditor` STEG 10b Major-1).
5. **Inför ingen tredje status för orphans** vid login. Den vore inte ett enumereringsorakel (403:an
   kräver rätt lösenord), men den är tillståndsberoende copy som säger *"ditt konto är trasigt"* — och
   under Beslut 1 är en orphan inget trasigt konto, den är **inget konto**. Den inbjuder supportlast
   utan handlingsbar nytta.
6. **Ta inte bort resend-knappen** vid HEM D, och ta inte bort login-knappen ur HEM C.
7. **Skriv ingen tredje prosavariant.** `security-auditor` band det i #1438 (`:89-90`) för den PR:ens
   tak; jag för över **disciplinen**, inte taket: HEM C/D är strykningar plus **en** självvald
   villkorssats. Botten är strykning.
8. **Fila ingen issue.** Se Beslut 2.

## Netto

**0 issues filade.** PR 3 = HEM C + HEM D, ett change-reason: *de kvarvarande auth-ytorna påstår bara
vad deras egen trigger fastställer, och account-exists-notisen bär återhämtningsvägen — utan att någon
yta blir tillståndsberoende.* R1 stängs **delvis**; resten bor på den uniforma 401:an och är ratificerad.
