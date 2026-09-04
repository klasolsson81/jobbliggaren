# Registration gate — opening it, and creating the first accounts

**Scope:** `Auth:RegistrationsOpen` and `Auth:RequireEmailConfirmation` on the deployed
box, and the only sanctioned way to bring an account into existence there. Owned by
[#734](https://github.com/klasolsson81/jobbliggaren/issues/734).
**Host:** Netcup RS 1000 G12, Debian 13 (trixie), Nuremberg — `dev.jobbliggaren.se`.
**Related:** [`vps-deploy-stack.md`](vps-deploy-stack.md) (the stack, and verification row
23) · [`master-key-ops.md`](master-key-ops.md) (the injection this depends on) ·
`deploy/.env.example` (the keys themselves, with their defaults and failure modes).

---

## 1. The model, in one paragraph

Registration is closed by default and the default is not an omission: the app becomes
publicly reachable before its legal and security gates are green, so an unset value must
fail closed. `AuthOptionsValidator` exempts Development and Test and applies everywhere else,
where it refuses the boot on an open gate without email confirmation, and again on an open
gate whose sender cannot deliver — so the two flags and the mail provider are one interlock,
not three switches. **Writing an account straight into
the database is not an alternative to this procedure**; `AuthOptions`' own documentation
forbids it, and this file is the path it prescribes instead.

## 2. Preconditions

1. **The email flip is done — and taking it is not this runbook's step.**
   `EMAIL_PROVIDER=Scaleway` with both credentials injected and the region set, per
   `deploy/.env.example`'s outbound-email block, whose inject-before-you-edit order is not
   restated here. **Setting that value IS the prod flip:** `release-checklist.md` §2.5 is the
   gate on it and Klas is the only one who may take it, never CC — so this precondition is
   not CC-satisfiable, and nothing below discharges it. Taking the flip belongs to
   [#183](https://github.com/klasolsson81/jobbliggaren/issues/183); this runbook only needs it
   to already be done. Read §2.5 itself for where that gate stands. Unsatisfied — including a
   flip taken outside that gate — this procedure does not start. Under `Console` the api
   resolves `NullEmailSender`, which cannot deliver, and opening the gate is a boot refusal.
2. **The Scaleway artifacts exist:** a Transactional Email API key (secret key) and the
   project id, generated in the Scaleway console. Producing them is the operator's step and
   belongs to [#183](https://github.com/klasolsson81/jobbliggaren/issues/183); this runbook
   only needs them to already exist.
3. **Two real, external mailboxes, and the second must be an alias of the first.** The
   domain's MX is `blackhole.tem.scaleway.com` — `@jobbliggaren.se` receives nothing, so a
   confirmation link sent there is unrecoverable. Use an ordinary external address for the
   operator account, and a `+`-suffixed alias of that same inbox for the CC account. The
   alias is required rather than merely convenient: `release-checklist.md`'s reinstatement
   schedule turns on **an address the controller himself holds**, so a `+`-suffixed alias of the
   operator's own inbox satisfies (b) and (d) by one property rather than by two clauses that
   happen to intersect. A separate mailbox satisfies neither, whatever it is called.
4. **The K2 edge credentials** (`BASIC_AUTH_USER` / `BASIC_AUTH_HASH`), because every
   request to the site — including the one the confirmation link makes — is challenged
   first.
5. **A rights channel that receives, or a recorded decision that it does not.**
   `kontakt@jobbliggaren.se` is the published Art. 12 controller contact and the Art. 15–22
   channel, and it is Reply-To on every message this procedure causes to be sent — while the
   apex MX is a blackhole, so both replies and rights requests are discarded silently.
   **`release-checklist.md` owns the escalation schedule and it was rewritten on 2026-08-16
   against a measured operating state — read it there, not here.** Its trigger (a) is
   `RegistrationsOpen=true` outside Development, which is what step 2 does, so this procedure
   reaches it by design; its (b) turns on **an address the controller himself holds**, which is
   what precondition 3's `+`-alias requirement guarantees and what step 5 registers — a role
   label such as "CC verification address" earns no exemption on its own. **Its measurements expire: the
   checklist says to re-measure (a) and (c) at the flip rather than inherit them**, so confirm
   there that the schedule still reads as it did before setting the knob. Either the mailbox
   receives, or the policy publishes a channel that does
   ([#183](https://github.com/klasolsson81/jobbliggaren/issues/183) owns the mailbox) — or Klas
   accepts the risk for this recipient set and records it, **which is his decision alone**.
   Whichever applies, it is written down before the knob is set.
   ⚠ **The K2 credential carries more than one GDPR conclusion, and this file counts none of
   them** — the grading and its ground live in `release-checklist.md` §2.5 point 1 leg (e)
   precondition 5, and the operator meets them in full on the `basic_auth` directive itself in
   `deploy/caddy/Caddyfile`. Read them there. The checklist's re-grading rests in
   part on the site answering `401` on every path; removing Basic auth for a demo makes the
   blackhole blocking in the same moment, and nothing warns. Treat the credential in
   `docs/test-accounts.local.md` accordingly.
6. **Registration collects personal data before it explains itself.** `/registrera` carries
   no Art. 13 first-layer notice and no link to the policy; the footer link is site chrome,
   not a collection-point notice. While both accounts are Klas's own, controller and data
   subject coincide and this is not a breach — **it becomes one at the first registrant who
   is not Klas.** So the gate is opened for the operator's own two accounts only, and closed
   again afterwards (step 10), until the notice ships.

## 3. The visit

Ordered, and the order is load-bearing at steps 0 and 7.

**0. Bring the box's clone up to date.**

```bash
cd /opt/jobbliggaren && sudo git pull --ff-only
```

Nothing does this for you. The hourly reconcile unit reconciles **images** from GHCR and
applies the compose file it finds on disk; it runs no `git` at all. Until this pull, the
compose file on the box has no `Auth__*` passthrough and the knobs below reach nothing —
they would sit in `.env` looking set, and the gate would stay closed with no error.

**1. The mail credentials and the provider value are already in place — precondition 1, and
nothing this procedure runs.** The injection order, the flip and its gate belong to
`deploy/.env.example`'s outbound-email block; the sequence against step 2's two `AUTH_` keys
is fixed in that file's **registration-gate** block, which is where to read it.

**2. Edit `deploy/.env`** — these three keys, and no `EMAIL_*` line:

```
AUTH_REGISTRATIONS_OPEN=true
AUTH_REQUIRE_EMAIL_CONFIRMATION=true
ADMIN_BOOTSTRAP_INITIAL_ADMIN_EMAIL=<the operator's own address>
```

**3. Restart the api.** Only `api` reads the three keys above, and step 2 changes no `EMAIL_*`
line, so `worker` — which shares those through the `x-app-email` anchor and consumes no
`Auth__*` at all — has no cause to restart here.

```bash
cd /opt/jobbliggaren/deploy && sudo docker compose -f docker-compose.yml up -d --pull never api
```

**4. Read the gate's own line — do not infer the posture from a healthy container.**

```bash
sudo docker logs jobbliggaren-api 2>&1 | grep 'Registration gate'
```

Expect, at **Warning** level:

```
Registration gate: OPEN outside Development; email confirmation: REQUIRED
```

`OPEN` with `NOT REQUIRED` is the one combination the validator refuses, so a container that
came up at all is in one of the other three and this line says which. If the api is instead
crash-looping, read the refusal: it names the offending key and the rule.

Expect **also**, on this boot, a Warning from the admin seeder saying no matching user was
found. That is correct: the address in `ADMIN_BOOTSTRAP_INITIAL_ADMIN_EMAIL` has no account
yet. Step 7 is what resolves it.

**5. Register both accounts** in a browser at `https://dev.jobbliggaren.se/registrera`,
through the K2 challenge: the operator's own account first (the address from step 2), then
the standing CC test account. Each returns `202` with no session — that is the
email-confirmation-first flow, not a failure. The `202` is deliberately uniform: a fresh and
an already-taken address are byte-identical on status and body, and only the mail differs.
So expect a confirmation link; **an "account already exists" notice means the address was
taken** — stop and find out by whom rather than retrying.

**6. Confirm both, from the links in the two inboxes.** The link points at
`https://dev.jobbliggaren.se`, so K2 challenges again — on whatever device opens the mail.
That is the edge gate working, not a broken link. Until a link is followed, that account's
login is refused with `403 EmailNotConfirmed`.

**7. Restart the api once more, so the admin role is assigned.**

⚠ **`restart` is correct here ONLY if `ADMIN_BOOTSTRAP_INITIAL_ADMIN_EMAIL` has not changed since
step 3's `up`. If it has, use the re-create form below instead.** A container's environment is
fixed at creation, so a restart re-runs the seeder against the value the container already holds.
Measured 2026-08-16: the address *had* been changed after step 3 — the plain one was burned by a
failed registration and the account was re-created under a `+`-alias — so a `restart` would have
assigned Admin to the wrong account, one that was itself scheduled for deletion. Check before you
choose:

```bash
sudo docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' jobbliggaren-api \
  | grep AdminBootstrap
```

If that value is the account you registered, `restart` is enough:

```bash
sudo docker restart jobbliggaren-api
```

If it is not, re-create instead — same command as step 10:

```bash
cd /opt/jobbliggaren/deploy && sudo docker compose -f docker-compose.yml up -d --pull never api
```

This is the sanctioned exception to *"manual applies go through the unit"* —
[`vps-deploy-stack.md`](vps-deploy-stack.md) §3b carries it, including the precondition it
requires and why the reconcile unit is the wrong instrument here. Check the precondition first.

`IdempotentAdminRoleSeeder` runs at **startup** and only then: it assigns the Admin role to
whichever account matches `ADMIN_BOOTSTRAP_INITIAL_ADMIN_EMAIL`, and at step 3 that account
did not exist. Confirm in the log that it found one this time — the seeder logs the user id,
never the address.

**Then blank the knob — and RE-CREATE, not restart.**

```bash
cd /opt/jobbliggaren/deploy && sudo docker compose -f docker-compose.yml up -d --pull never api
```

⚠ **`docker restart` cannot do this step and will report success.** A container's environment is
fixed at creation, so `restart` re-runs the process against the value it already had: the address
stays in container env, the seeder keeps re-asserting the role on every start, and the operator
believes the knob is blanked because `.env` says so. Only a re-create re-reads `.env`. Measured
2026-08-16, where the same asymmetry bit in the other direction first — the value had been
*changed* after step 3's `up`, so the running container still carried the old address and a
`restart` would have granted Admin to the wrong account.

The seeder re-asserts on **every** start, so a
standing value is not a bootstrap but a permanent grant: it silently re-grants the role after
any in-app revocation, and it would hand Admin to a future holder of that address. The role
is persisted in the database, so the knob has no further work once the log confirms the
assignment. Blanking it also takes a real address back out of container environment, where
`docker inspect` and the container's on-disk config both carry it. Verify with
`sudo docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' jobbliggaren-api | grep AdminBootstrap`
— expect the key with an empty value. ⚠ **Read the key, never a count over the whole inspect
output.** A `grep -c` for the address's **local part** still matches the image reference
(`ghcr.io/<owner>/…` carries the GitHub account), so it returns non-zero on a correctly blanked
knob and reads as "the address is still there". Measured 2026-08-16, where it did exactly that.
The full address does **not** match — the image carries no `@domain` — so the trap is specific to
grepping the local part, which is the natural thing to reach for.

**8. Rotate the bootstrap password — only if one was handled outside the app.** Log in and change
it there. ⚠ **Under this procedure that is normally not the case, and the step is then a no-op.**
It is inherited from the hand-seeded model §1 forbids, where an operator sets a password before
the account exists. Step 5 registers in a browser, so the password was chosen *in* the app and has
never been outside it. Rotate anyway if it was pasted from somewhere durable; otherwise skip, and
do not read the skip as an outstanding action.

**9. Record the test account.** Fill `docs/test-accounts.local.md` in the main checkout from
its tracked template (`docs/test-accounts.local.md.example`). It is gitignored and stays
that way: this repo is public, and the file carries both the CC account's password and the
K2 credential. It is deliberately **not** synced into worktrees.

**10. Close the gate again — and RE-CREATE, exactly as in step 7.** Comment out
`AUTH_REGISTRATIONS_OPEN`, then:

```bash
cd /opt/jobbliggaren/deploy && sudo docker compose -f docker-compose.yml up -d --pull never api
```

⚠ **`docker restart` cannot close the gate and will report success.** Same mechanism as step 7 and
higher stakes: compose substitutes `Auth__RegistrationsOpen: ${AUTH_REGISTRATIONS_OPEN:-false}` at
container *creation*, so a restart re-runs the process against the env it already has. Step 7's
second half re-created the container **while the gate line was still set**, so at this point the
live container definitely carries `true` — there is no rescuing re-create between the two steps.
Commenting the line out and restarting leaves the gate **open** while `.env` says closed and the
operator believes it is closed.

**Then read the gate's own line, exactly as step 4 does. This step is not done until it says
`CLOSED`:**

```bash
sudo docker logs jobbliggaren-api 2>&1 | grep 'Registration gate'
```

Expect `Registration gate: CLOSED; email confirmation: REQUIRED` — EventId 4300 at Information,
not 4301 at Warning.

**Then make the gate answer for itself. This step is not done until it does.** The log line states
the posture the process **booted with**; this measures the posture the endpoint **enforces**, and
ADR 0132 Leg 2 is bounded by the second. A `POST` to `/api/v1/auth/register` **must** answer
`503 Auth.RegistrationsClosed` and leave no row behind — the gate is the handler's first
statement.

**Use this form. The naive one cannot answer 503 and its failure looks like the thing you are
testing for.** `RegisterCommand` is a complex type, so it is body-bound and model binding runs
*before* the handler: a bare `curl -X POST` returns **415** and malformed JSON returns **400**,
neither of which ever reaches the gate. From inside the project network, where K2 does not apply:

**Take the baseline first**, or the second half of this check has nothing to compare against:

```bash
sudo docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -tAc \
  'select (select count(*) from identity."AspNetUsers"), (select count(*) from public.job_seekers);'
```

Then probe, **with a body that passes validation**:

```bash
printf '{"email":"probe@example.com","password":"%s","displayName":"probe"}' \
  "$(openssl rand -base64 18)" \
  | sudo docker exec -i jobbliggaren-caddy curl -sS -X POST \
      http://api:8080/api/v1/auth/register -H 'Content-Type: application/json' \
      -d @- -w '\nHTTP %{http_code}\n'
```

**The body is built on the host and piped in, and both halves of that are deliberate.** `openssl`
exists on the box and **not** in the caddy image (measured — the same command inline returns
`sh: openssl: not found`, then `400` for a zero-length password), so the generation has to happen
outside the container. And piping through `-d @-` keeps the value out of `sudo`'s argv, which is
where #1343's whole defect class lives; a literal in this file would be worse still — a
password-shaped string in a public repo, which `gitleaks` flags, correctly. Nothing consumes it
either way: the gate refuses before `CreateUserAsync`. It only has to clear
`RegisterCommandValidator`'s 12-character minimum, which `openssl rand -base64 18` does by
construction.

Expect `HTTP 503` with `"title":"Auth.RegistrationsClosed"`.

⚠ **An empty `{}` does NOT work, and its failure is indistinguishable from the thing you are
testing for.** The gate is the handler's first statement — but `ValidationBehavior` is a *pipeline
behavior wrapping the handler*, so `RegisterCommandValidator` runs first and an empty object comes
back **400** with `'Email' must not be empty`, never reaching the gate at all. Measured both ways
on 2026-08-16. "Before validation" was never the claim; `RegisterCommandHandler`'s own comment says
what first means — *"FIRST statement, before `CreateUserAsync`"*, i.e. before the account is
created, not before the pipeline. The address above is deliberately `example.com`, reserved by
RFC 2606 and belonging to nobody.

⚠ **A `429` is a fifth non-503 answer**:
`/register` runs under `AuthWritePolicy`, and you have just registered accounts through it. Wait
out the window rather than reading the throttle as a closed gate.

**Then re-run the baseline query — "leaves no row behind" is a claim, not an observation.** Both
counts must be **identical** to the ones taken before the probe. That is what makes the 503
meaningful: a validating body reaches the gate, and the gate refuses before `CreateUserAsync`, so
a refusal that left a row would be a defect rather than a posture. After a two-account visit the
pair reads `2 | 2`, but compare against your own baseline rather than that number — a burned
address or an earlier orphan moves it.

⚠ **It is mandatory rather than a nicety because every failure mode in this step looks identical
from the outside.** A `restart` that changed nothing, a reconcile whose lock branch exited 0
having applied nothing, a refused image, the right log line read at the wrong moment — all of them
present as "the gate is still open", and this is the only check in the procedure that does not
depend on which command applied the change.

This command is the sanctioned exception to *"manual applies go through the unit"* —
[`vps-deploy-stack.md`](vps-deploy-stack.md) §3b carries it, including the precondition it
requires and why the reconcile unit is the wrong instrument here. **Check that precondition before
running it.**

**Leave `AUTH_REQUIRE_EMAIL_CONFIRMATION=true` set** (`.env.example` says why: with the gate
closed a `false` there is accepted silently and disables the login gate). Accounts and logins
survive a closed gate; closing it refuses new registrations only.

Closed is the default rather than a preference, and preconditions 5 and 6 are the reason:
until the rights channel receives and `/registrera` carries its Art. 13 notice, the gate is
opened for a visit and not left open between them. Leaving it open is available, but it is a
deliberate exception with K2 as the only thing in front of it — and K2's plaintext now sits in
a file whose audience is every future CC session.

## 4. Verification row 23's second half

The reason this procedure exists on the critical path. Row 23 in
[`vps-deploy-stack.md`](vps-deploy-stack.md) asks for one encrypted field read back through
the app. ✅ **DONE 2026-08-16 — this section is a standing procedure for future visits now, not
an outstanding task.** It ~~has been~~ **was** blocked on the box having no users at all rather
than deferred by choice; the first visit created the accounts and row 23 carries both halves.
⚠ **Read that row's cell for what the measurement does and does not establish.** It evidences
encrypt-on-write and decrypt-on-read under the live generation; it does **not** reach the
fresh-DEK re-wrap case the row's Instrument column names, which needs a field written under one
master-key generation and read under the next. That is still owed, and the next rotation over a
non-empty `user_data_keys` is what will make it testable.

**Write it on a surface that actually crosses the DEK path.** The encrypted set is
`Application.CoverLetter`, `ApplicationNote.Content`, `FollowUp.Note` and the CV fields
(`ParsedResume.RawText`/`Content`, `ResumeVersion.Content`) — `EncryptedFieldRegistry` is the
authority. **A profile field is not among them**: `JobSeeker` has no encrypted column, so
writing a display name leaves `user_data_keys` at 0 and would either look like a broken DEK
path or tick this row on nothing. Use a cover letter on `/ansokningar`, or a CV import on
`/cv`.

Then read it back on a fresh page load, and check that `user_data_keys` has gone from 0 to 1.
Record what you ran and what it returned — the row is stamped from that. ⚠ **A page load and an
API call are not the same instrument**, and 2026-08-16 measured the API half only (curl inside
the project network) with the browser half operator-attested. If you take the API route, say so
in the cell rather than letting "through the app" cover both.

## 5. Rollback

Every *configuration* failure above is a boot refusal whose message names the key and the
rule, so the recovery is one shape: revert the offending line in `deploy/.env` and restart. A
refused boot creates nothing, and a **gate-closed** refusal (503) leaves no Identity user, no
job seeker and no audit row — the gate is the handler's first statement, so that holds by
construction.

**Three failures above are not boot refusals, and none of them leaves an account behind.**

- **Step 0 skipped** — silent, and safe: the knobs reach nothing and the gate stays closed.
- **Step 6 never completed** — `403 EmailNotConfirmed` on an account that exists. Follow the
  link; there is nothing to revert.
- **A Scaleway credential that is present but wrong**, or a From identity outside the
  verified domain. This one is by design and it is the one to know: validation at boot checks
  that the keys are *present*, and `ScalewayEmailSender` reports itself able to deliver
  unconditionally, so the validator's sender rule passes and the boot succeeds.

  **It is silent at step 5.** The registration answers the same **202** as a working send and
  the account is created in full — never a 500, and never the boot log, which will look clean.

  **The api's runtime log carries two lines, and the first is the one to read:**

  1. `[ScalewayEmailSender] {EmailKind} email FAILED ({ErrorType}, HTTP {HttpStatus})` —
     **Error**, EventId 3006, written just before the adapter wraps the fault. `HttpStatus` is
     the diagnostic: it separates a wrong key (401) from a wrong project (403) from a From
     identity outside the verified domain. That is the discriminator for the two causes this
     bullet opens with, so read it before reaching for Scaleway's console.
  2. `RegisterCommand: confirmation send failed …` — **Warning**, naming `#1349`. It records
     that the account stands and the link did not go out; it carries no status.

  Neither line carries a recipient or a body (ADR 0124). Confirm the delivery attempt itself in
  Scaleway's own delivery log.

  **Recovery is the user's own:** the activation mail never arrived, so they press
  **"Skicka en ny bekräftelselänk"** — mounted on both the login and the registration screen.
  It is a link, not a code. The account is real, so the resend works.

  ⚠ **This bullet described the opposite until #1349 (2026-08-17)**, and the difference matters
  if you are diagnosing an older incident: the send used to be the handler's final *unguarded*
  action, so the fault surfaced as a 500 and rolled the not-yet-committed job seeker back while
  the Identity user — committed in its own boundary — survived. That orphan was then swept by
  the account-hard-delete job hours later. The send is now swallowed and the job seeker commits,
  so **this path produces no orphan.** Registration still passes through that state transiently
  on every call, and other producers remain; `OrphanedIdentityActivationTests` enumerates them.
