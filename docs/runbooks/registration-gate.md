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
fail closed. `AuthOptionsValidator` refuses a Production boot on an open gate without email
confirmation, and again on an open gate whose sender cannot deliver — so the two flags and
the mail provider are one interlock, not three switches. **Writing an account straight into
the database is not an alternative to this procedure**; `AuthOptions`' own documentation
forbids it, and this file is the path it prescribes instead.

## 2. Preconditions

1. **The email flip is done.** `Email__Provider=Scaleway` with both credentials injected
   and the region set, per `deploy/.env.example`'s outbound-email block — follow that
   block's own inject-before-you-edit order; it is not restated here. Under `Console`
   (today's default) the api resolves `NullEmailSender`, which cannot deliver, and opening
   the gate is a boot refusal.
2. **The Scaleway artifacts exist:** a Transactional Email API key (secret key) and the
   project id, generated in the Scaleway console. Producing them is the operator's step and
   belongs to [#183](https://github.com/klasolsson81/jobbliggaren/issues/183); this runbook
   only needs them to already exist.
3. **Two real, external mailboxes.** The domain's MX is `blackhole.tem.scaleway.com` —
   `@jobbliggaren.se` receives nothing, so a confirmation link sent there is unrecoverable.
   Use ordinary external addresses; a `+`-suffixed alias of an existing inbox works for the
   second account.
4. **The K2 edge credentials** (`BASIC_AUTH_USER` / `BASIC_AUTH_HASH`), because every
   request to the site — including the one the confirmation link makes — is challenged
   first.

## 3. The visit

Ordered, and the order is load-bearing at steps 0, 1 and 7.

**0. Bring the box's clone up to date.**

```bash
cd /opt/jobbliggaren && sudo git pull --ff-only
```

Nothing does this for you. The hourly reconcile unit reconciles **images** from GHCR and
applies the compose file it finds on disk; it runs no `git` at all. Until this pull, the
compose file on the box has no `Auth__*` passthrough and the knobs below reach nothing —
they would sit in `.env` looking set, and the gate would stay closed with no error.

**1. Inject the mail credentials before editing `.env`.** Per the email block's order:
setting `EMAIL_PROVIDER=Scaleway` while the files are absent is itself a boot refusal, so
editing first takes the stack down and the injection then happens under an outage.

**2. Edit `deploy/.env`** in one pass — the email lines per that block, then:

```
AUTH_REGISTRATIONS_OPEN=true
AUTH_REQUIRE_EMAIL_CONFIRMATION=true
ADMIN_BOOTSTRAP_INITIAL_ADMIN_EMAIL=<the operator's own address>
```

**3. Restart the two services that read them.**

```bash
cd /opt/jobbliggaren/deploy && sudo docker compose -f docker-compose.yml up -d api worker
```

**4. Read the gate's own line — do not infer the posture from a healthy container.**

```bash
docker logs jobbliggaren-api 2>&1 | grep 'Registration gate'
```

Expect, at **Warning** level:

```
Registration gate: OPEN outside Development; email confirmation: REQUIRED
```

`OPEN` with `NOT REQUIRED` is not reachable here — the validator refuses that boot — so a
container that came up at all has one of two postures, and this line says which. If the api
is instead crash-looping, read the refusal: it names the offending key and the rule.

Expect **also**, on this boot, a Warning from the admin seeder saying no matching user was
found. That is correct: the address in `ADMIN_BOOTSTRAP_INITIAL_ADMIN_EMAIL` has no account
yet. Step 7 is what resolves it.

**5. Register both accounts** in a browser at `https://dev.jobbliggaren.se/registrera`,
through the K2 challenge: the operator's own account first (the address from step 2), then
the standing CC test account. Each returns `202` with no session — that is the
email-confirmation-first flow, not a failure.

**6. Confirm both, from the links in the two inboxes.** The link points at
`https://dev.jobbliggaren.se`, so K2 challenges again — on whatever device opens the mail.
That is the edge gate working, not a broken link. Until a link is followed, that account's
login is refused with `403 EmailNotConfirmed`.

**7. Restart the api once more, so the admin role is assigned.**

```bash
docker restart jobbliggaren-api
```

`IdempotentAdminRoleSeeder` runs at **startup** and only then: it assigns the Admin role to
whichever account matches `ADMIN_BOOTSTRAP_INITIAL_ADMIN_EMAIL`, and at step 3 that account
did not exist. Confirm in the log that it found one this time — the seeder logs the user id,
never the address.

**8. Rotate the bootstrap password.** Log in and change it in the app. A password chosen
before the account existed has been handled outside the app; the in-app change closes that.

**9. Record the test account.** Fill `docs/test-accounts.local.md` in the main checkout from
its tracked template (`docs/test-accounts.local.md.example`). It is gitignored and stays
that way: this repo is public, and the file carries both the CC account's password and the
K2 credential. It is deliberately **not** synced into worktrees.

**10. Decide the end posture, explicitly.** Leaving the gate open means anyone reaching the
site behind K2 can create an account; closing it again is `AUTH_REGISTRATIONS_OPEN` back to
commented plus a restart. **Accounts and logins survive a closed gate** — closing it refuses
new registrations only. Whichever you choose, choose it; the Warning line recurs on every
boot by design and is not a reminder to act.

## 4. Verification row 23's second half

The reason this procedure exists on the critical path. Row 23 in
[`vps-deploy-stack.md`](vps-deploy-stack.md) asks for one encrypted field read back through
the app, and it has been blocked on the box having no users at all rather than deferred by
choice. With an account in hand: log in, write a profile field that goes through the DEK
path, read it back on a fresh page load, and check that `user_data_keys` has gone from 0 to
1. Record what you ran and what it returned — the row is stamped from that, in its own
change.

## 5. Rollback

Every failure mode above is a boot refusal whose message names the key and the rule, so the
recovery is always the same shape: revert the offending line in `deploy/.env` and restart.
There is no partial state to unwind — a refused boot creates nothing, and a refused
registration leaves no Identity user, no job seeker and no audit row behind.
