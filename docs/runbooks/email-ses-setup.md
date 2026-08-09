# Amazon SES setup — making the domain able to send

**Scope:** the DNS and AWS-side provisioning that turns `jobbliggaren.se` into a domain Amazon
SES may send from, and the ordering constraints that make it survive the domain's existing
`p=reject` DMARC policy. It stops at the point where `Email:Provider` would be flipped; the
flip itself is [`release-checklist.md`](./release-checklist.md) §2.5 and is Klas's alone.
**Owned by** [#183](https://github.com/klasolsson81/jobbliggaren/issues/183) (ADR 0124; ADR 0080
prod-flip checklist).
**Related:** [`release-checklist.md`](./release-checklist.md) §2.5 (the gate) ·
[`vps-deploy-stack.md`](./vps-deploy-stack.md) §5 (verification rows 33–38) ·
[`aws-setup.md`](./aws-setup.md) (the `jobbpilot` SSO profile; local-only, gitignored).
**Authority:** ADR 0124 and `release-checklist.md` §2.5. Where this runbook and an ADR disagree,
the ADR wins and this file is wrong.

> **THE DNS HALF OF §3 HAS NOT BEEN EXECUTED, AND NOTHING HAS EVER BEEN SENT. Read §3 step 4
> onward as a design, not as a report.**
>
> The AWS-side calls in §3 steps 1–3 and §3 step 6 **have** run — 2026-08-09, and the values in
> this file are their real output, not placeholders. What has not happened is the DNS
> publication at STRATO, which is Klas's and is the only thing that can move
> `DkimAttributes.Status` off `PENDING`. Until it does, SES holds a domain identity it cannot
> sign for, and the product still sends nothing. When the records are published and verified,
> replace this note with the date and fill verification rows 33–38 in
> [`vps-deploy-stack.md`](./vps-deploy-stack.md) §5.

---

## 1. The model, in one paragraph

SES will send as `no-reply@jobbliggaren.se`, so the identity that has to exist in SES is the
**apex domain**, not a subdomain and not an address. Verifying a domain identity is done with
Easy DKIM: SES generates three tokens, you publish three CNAME records under the domain, and
when SES can resolve all three it considers the domain verified and signs every outgoing message
with `d=jobbliggaren.se`. That signature is the entire delivery story here, because the domain
already publishes `v=DMARC1;p=reject;` — a message that fails DMARC is **rejected outright, not
spam-foldered**. DMARC passes on either SPF alignment or DKIM alignment, and for SES mail SPF
cannot align without a custom MAIL FROM domain (§5 — the default envelope is a subdomain of
`amazonses.com`), so DKIM is the only aligning mechanism in play here. The domain already works
this way: Klas's ordinary mail from `@jobbliggaren.se` goes through STRATO and is signed by
STRATO's own selectors, which is why `p=reject` has not been breaking it. Adding SES means
adding a second, independent set of DKIM selectors under the same domain — additive, and
invisible to the first.

---

## 2. State this file was written against

Every number below is the output of the command beside it. Re-run them rather than trusting the
text; a value without its command is a claim that cannot be told from one that has decayed.

```bash
# AWS account and SES posture. Run from anywhere with the jobbpilot SSO profile.
aws sesv2 get-account --profile jobbpilot --region eu-north-1
# expect (2026-08-09): ProductionAccessEnabled false — the sandbox — with Max24HourSend 200,
# MaxSendRate 1, SendingEnabled true, EnforcementStatus HEALTHY.

aws sesv2 list-configuration-sets --profile jobbpilot --region eu-north-1
# expect: an empty list. This is release-checklist.md §2.5 point 1 precondition 4 read at the
# account level; the identity-level half is §7 below.
```

```bash
# DNS, read against a resolver that is not the registrar's own.
nslookup -type=TXT jobbliggaren.se 8.8.8.8          # expect: no TXT answer at all — no SPF
nslookup -type=TXT _dmarc.jobbliggaren.se 8.8.8.8   # expect: v=DMARC1;p=reject;  (no rua=)
nslookup -type=MX  jobbliggaren.se 8.8.8.8          # expect: smtp.rzone.de
nslookup -type=TXT strato-dkim-0002._domainkey.jobbliggaren.se 8.8.8.8   # expect: v=DKIM1; k=rsa; ...
nslookup -type=TXT strato-dkim-0003._domainkey.jobbliggaren.se 8.8.8.8   # expect: v=DKIM1; k=ed25519; ...
```

**The two STRATO selectors are the load-bearing measurement in this file.** They are why
`p=reject` is survivable today, and they are what the SES work must not disturb. They are
STRATO's documented selector names, so their presence is expected rather than surprising — but
it is measured here because the whole SPF decision in §5 rests on it.

**There is also a wildcard MX.** A query for a label that cannot exist returns one:

```bash
nslookup -type=MX zz9x-probe.jobbliggaren.se 8.8.8.8   # expect: smtp.rzone.de
```

That matters only in §6, and it is measured here so that §6 does not have to assume it.

---

## 3. Make the domain able to send

### Step 1 — authenticate

```bash
aws sso login --profile jobbpilot --no-browser
```

This prints a URL. It must be opened in a browser **on the same machine**, because the callback
goes to `127.0.0.1`. Sessions last 12 hours per the SSO profile's own configuration —
[`aws-setup.md`](./aws-setup.md) §1 carries the profile details, and that file is **local-only**
(gitignored, ADR 0072): a reader on GitHub or in a fresh worktree will not find it, and the
command above is complete without it.

### Step 2 — create the domain identity

```bash
aws sesv2 create-email-identity \
  --email-identity jobbliggaren.se \
  --profile jobbpilot --region eu-north-1
```

The domain is the **apex**. `no-reply@jobbliggaren.se` is the sender — the default in
`EmailOptions.FromAddress`; `SesEmailSenderTests` pins the From-header *composition*
`FromName <FromAddress>`, not that value, which itself lives unpinned in `EmailOptions.cs` —
and a domain identity covers every address under it, so no address identity is needed for
sending. Easy DKIM with a 2048-bit key is
the SES default and is what we want; no `--dkim-signing-attributes` is passed.

Ran 2026-08-09. Output carried `DkimAttributes.Status: NOT_STARTED`, three `Tokens`, and
`SigningHostedZone: dkim.amazonses.com`.

### Step 3 — read the tokens and the hosted zone out of the response

If the response above has scrolled away:

```bash
aws sesv2 get-email-identity \
  --email-identity jobbliggaren.se \
  --profile jobbpilot --region eu-north-1 \
  --query 'DkimAttributes.{Status:Status,Zone:SigningHostedZone,Tokens:Tokens}'
```

**Do not hardcode the CNAME suffix from memory or from another project.** AWS documents the
value as *"the DKIM token followed by a hosted zone domain (for example,
`{{token}}.dkim.amazonses.com` or `{{token}}.{{a31d}}.dkim.{{us-west-2}}.amazonses.com`). The
hosted zone portion varies by AWS Region and cell."* It is `SigningHostedZone` in the response
and nowhere else. For this identity it resolved to the short form, which is why the table below
looks like the common case — that is an observation about this identity, not a rule.

### Step 4 — publish the three CNAME records at STRATO

STRATO: **Domains → Domainverwaltung → DNS**, then add three CNAME records.

**Enter the prefix only. STRATO appends the domain name itself** — its own documentation says
*"der für 'Präfix' eingegebene Wert automatisch um den Domainnamen ergänzt wird"*, and AWS warns
about exactly this provider behaviour: *"Make sure that your provider didn't automatically append
your domain name to the Name/host value."* Pasting the fully-qualified name produces
`<token>._domainkey.jobbliggaren.se.jobbliggaren.se`, which resolves to nothing and fails
silently for up to 72 hours.

| STRATO "Präfix" (type CNAME) | Value |
|---|---|
| `wxsvjat5qoockft4lvfyimv734imai5w._domainkey` | `wxsvjat5qoockft4lvfyimv734imai5w.dkim.amazonses.com` |
| `vwts4nge2rzcdl2zvqxw3wmofzyj3c5n._domainkey` | `vwts4nge2rzcdl2zvqxw3wmofzyj3c5n.dkim.amazonses.com` |
| `4s4biygclhewprir3bmlnvmxvhh2efc7._domainkey` | `4s4biygclhewprir3bmlnvmxvhh2efc7.dkim.amazonses.com` |

Two further notes, both from AWS's own troubleshooting list: the leading underscore in
`_domainkey` is required and must not be doubled (`_<token>._domainkey` is wrong), and these
values are **per-Region** — an identity in another Region would have different tokens.

These tokens are not secrets. They are published in public DNS by design.

### Step 5 — verify the records resolve, against the outside

```bash
for t in wxsvjat5qoockft4lvfyimv734imai5w vwts4nge2rzcdl2zvqxw3wmofzyj3c5n 4s4biygclhewprir3bmlnvmxvhh2efc7; do
  nslookup -type=CNAME "${t}._domainkey.jobbliggaren.se" 8.8.8.8
done
# expect, for each: canonical name = <same token>.dkim.amazonses.com
```

Query a public resolver, not the registrar's. What matters is what SES can see, not what the
STRATO control panel believes it saved.

Then let SES decide:

```bash
aws sesv2 get-email-identity --email-identity jobbliggaren.se \
  --profile jobbpilot --region eu-north-1 \
  --query '{Dkim:DkimAttributes.Status,Verified:VerifiedForSendingStatus}'
# expect eventually: Dkim SUCCESS, Verified true. AWS allows up to 72 h for DNS propagation.
```

### Step 6 — verify a recipient, because the account is in the sandbox

In the sandbox SES will only deliver **to** verified destinations. This is a restriction on the
recipient and has nothing to do with the sender: mail still goes out as
`no-reply@jobbliggaren.se`.

```bash
aws sesv2 create-email-identity \
  --email-identity klasolsson81@gmail.com \
  --profile jobbpilot --region eu-north-1
```

Ran 2026-08-09. AWS sends a verification message to that address; the link in it expires after
24 hours. Until it is clicked, `VerifiedForSendingStatus` stays `false` and sends to that address
are rejected.

**The sandbox is not a problem to be solved here.** It caps sending at 200/24 h and 1/s, which is
ample for one recipient, and it enforces at the account level what we want during dev anyway:
that nobody but Klas can receive mail from this system. Leaving it is §8.

---

## 4. DMARC aggregate reporting — and the address must be on this domain

The domain publishes `v=DMARC1;p=reject;` with no `rua=`, so mail that fails DMARC is rejected
and **no one is told**. Adding a reporting address does not change the policy; it is purely
additive.

| STRATO "Präfix" (type TXT) | Value |
|---|---|
| `_dmarc` | `v=DMARC1;p=reject;rua=mailto:dmarc@jobbliggaren.se` |

**The reporting address must be at `jobbliggaren.se`, and a Gmail address will not work.**
RFC 7489 §7.1 requires that a report destination outside the policy domain be authorised by a
TXT record at `<policy-domain>._report._dmarc.<destination-domain>`; without it the URI *"MUST be
ignored by the Mail Receiver generating the report"*. Measured 2026-08-09:

```bash
nslookup -type=TXT jobbliggaren.se._report._dmarc.gmail.com 8.8.8.8   # expect: NXDOMAIN
```

Google publishes no such record and it is not ours to create, so `rua=mailto:<anything>@gmail.com`
would produce a DMARC record that looks correct and delivers no reports at all. An address on
`jobbliggaren.se` needs no authorisation record. Create `dmarc@jobbliggaren.se` as a STRATO
mailbox or alias; forwarding it onward to Gmail afterwards is a mailbox rule and DMARC never
sees it.

One consequence to name before the mailbox exists rather than after: aggregate reports carry a
`source_ip` for every sending source, spoofers included, and an IP address can be personal data
(C-582/14 *Breyer*). Receiving them is ordinary network security (Art. 6(1)(f), recital 49) and
touches no user data — but the mailbox is a new inbound flow, and STRATO appears nowhere in the
policy's recipient list today. When verification row 38 is measured, name the legal basis and
the mailbox's retention wherever the flip's paperwork lands (security-auditor, 2026-08-09).

---

## 5. There is deliberately no apex SPF record — and what it would govern is not SES

**Do not "fix" the absence of an SPF record on the apex as part of the SES work.** It is a
decision, measured 2026-08-09 — on a narrower ground than an earlier version of this section
claimed, and the correction is load-bearing (security-auditor Major, 2026-08-09).

**SPF is evaluated against the envelope (MAIL FROM) domain, not the From header** (RFC 7208
§2.4). Without a custom MAIL FROM domain (§6.1), SES sends with an envelope on a subdomain of
`amazonses.com` — AWS: *"Messages that you send through Amazon SES automatically use a
subdomain of amazonses.com as the default MAIL FROM domain. SPF authentication successfully
validates these messages because the default MAIL FROM domain matches the application that sent
the email."* So SES mail already carries an SPF **pass** on `amazonses.com`, **an apex record
on `jobbliggaren.se` is never consulted for it, and publishing one can neither help nor harm
SES delivery.** What that pass cannot do is align: `amazonses.com` does not match the From
domain, so it contributes nothing to DMARC — which is why DKIM (§3) is the only *aligning*
mechanism SES mail has until §6.1 exists.

**What an apex SPF record does govern is STRATO's mail**, which sends with an envelope on
`@jobbliggaren.se`. Today that path has SPF `none` (no apex TXT at all, §2) and survives
`p=reject` on STRATO's DKIM selectors alone. STRATO's documented record,
`v=spf1 redirect=_spf.strato.com`, would give that mail an aligned SPF **pass** — a second
passing mechanism where today there is one:

```bash
nslookup -type=TXT _spf.strato.com 8.8.8.8   # expect: v=spf1 ip4:... ip6:... -all
```

Whether to publish it is therefore a question about the **existing** mail path, not about SES.
The two senders use different envelope domains and an apex record never collides with §6.1's
subdomain record. **That choice is Klas's and is escalated, not decided here.**

The decision this runbook makes is only this: **the SES lane does not touch the apex**, because
the one mail path an apex record affects is the one this lane must not disturb.

---

## 6. Deliberately not done yet

### 6.1 Custom MAIL FROM domain

ADR 0124 lists this as Klas's to provision, and it is not done. It would give SES mail SPF
**alignment** — the thing §5 explains the default `amazonses.com` envelope can never provide —
and put bounce handling on our own subdomain. It is **not** required for delivery, which is why
it is sequenced after §3 rather than beside it. Its SPF record lives on the subdomain and
governs only SES's envelope; it never collides with an apex record, which governs only
STRATO's (§5).

It has one trap that must be handled and is measured in §2: **`*.jobbliggaren.se MX` exists**, so
any subdomain already inherits an MX pointing at STRATO. SES requires the MAIL FROM subdomain's
MX to point at its own feedback endpoint, so an explicit MX must be published for that subdomain
to mask the wildcard. Choosing `BehaviorOnMxFailure` is part of that step and is not a default to
accept blindly.

### 6.2 The production IAM key

`Email:Ses:AccessKeyId`/`SecretAccessKey` take a long-lived static key; there is no instance role
on the box. It must be scoped to `ses:SendEmail` only. Before it is used,
`release-checklist.md` §2.5 point 1 precondition 1 requires:

```bash
aws sts get-caller-identity --profile <the-prod-key-profile>
# required: Account == 710427215829
```

**Running that with the SSO admin role does not discharge the precondition.** The requirement is
about the key that lands in configuration, and a measurement made with a different principal
answers a different question.

### 6.3 The flip

`Email:Provider=Ses` is not set by this runbook and never by CC. It is gated by
`release-checklist.md` §2.5. **Point 1 is not green; its legs and their statuses live in the
point itself**, and §2.5's own preamble instructs reading them there rather than from any
summary — including this one. Two mechanical prerequisites also have to exist first — the
`Email__*` variables set in `deploy/.env`, and the two credential files written on the box —
and §8's `Email__*` entry names both, along with the injection gap that owns the second.

---

## 7. Verifying it actually works

Verification rows **33–38** in [`vps-deploy-stack.md`](./vps-deploy-stack.md) §5 are the
protocol; this section is how they are produced.

**The control measurement comes first, and it is the point of §5.** After any DNS work, confirm
that the existing mail path is untouched:

```bash
nslookup -type=TXT strato-dkim-0002._domainkey.jobbliggaren.se 8.8.8.8   # unchanged
nslookup -type=TXT strato-dkim-0003._domainkey.jobbliggaren.se 8.8.8.8   # unchanged
nslookup -type=MX  jobbliggaren.se 8.8.8.8                               # expect: smtp.rzone.de
nslookup -type=TXT jobbliggaren.se 8.8.8.8                               # expect: still no TXT
```

**Identity-level configuration set**, which is `release-checklist.md` §2.5 point 1 precondition
4 and cannot be pinned by any test in the repo, because it is AWS-side state:

```bash
aws sesv2 get-email-identity --email-identity jobbliggaren.se \
  --profile jobbpilot --region eu-north-1
# expect: no ConfigurationSetName key in the response at all.
```

The measurement, its date and the re-measure obligation are protocolled in verification row 35
— read the outcome there, not here; this section carries only the command.

**A real send, which proves what no DNS query can.** It goes only to the verified recipient and
does not touch the application or the box, so it exercises no gate that §6.3 owns:

```bash
aws sesv2 send-email \
  --from-email-address 'no-reply@jobbliggaren.se' \
  --destination 'ToAddresses=klasolsson81@gmail.com' \
  --content 'Simple={Subject={Data=SES verification,Charset=UTF-8},Body={Text={Data=Test,Charset=UTF-8}}}' \
  --profile jobbpilot --region eu-north-1
```

Then open the received message's original headers and read the `Authentication-Results` line.
Required: `dkim=pass header.d=jobbliggaren.se` **and** `dmarc=pass`. A `dkim=pass` with a
`header.d` of anything else is not alignment and would not survive `p=reject`; reading only for
the word "pass" is how that distinction gets missed.

---

## 8. What this runbook does not own

- **The flip of `Email:Provider`** — [`release-checklist.md`](./release-checklist.md) §2.5, and
  Klas's alone. ADR 0124 states it unconditionally.
- **The AWS processing agreement and the Chapter V documentation** —
  [#183](https://github.com/klasolsson81/jobbliggaren/issues/183) and §2.5 point 1.
- **`Email__*` delivery into the box's containers** — the operator view (variables, defaults,
  what setting each does) lives in `deploy/.env.example`, and the anchor itself in
  `deploy/docker-compose.yml`. The two SES credential **files** the `_FILE` pointers name do
  not exist yet and nothing writes them: `deploy/systemd/jobbliggaren-inject-secrets.sh`
  carries a fixed fail-loud `SECRET_KEYS` array without the SES pair, so secrets *will* reach
  the containers through that script only once it is extended — which is the flip's work, not
  this runbook's. Both of §6.3's mechanical prerequisites resolve here.
- **Leaving the SES sandbox.** It is an application, not a payment: AWS requires the applicant to
  *"confirm that you have a process in place for handling bounce and complaint notifications"*,
  and no such process is built. Requesting production access before it exists would be attesting
  to something untrue.
- **Bounce and complaint handling itself** — no owner in code today.

---

## 9. Unmeasured, and named

1. **Whether the three CNAMEs verify.** As of 2026-08-09 they are not published, so
   `DkimAttributes.Status` is `PENDING` and no send can succeed. Everything in §3 step 4 onward
   is derived from AWS's documentation, not observed here.
2. **Whether the recipient identity was confirmed.** The verification link expires 24 hours after
   2026-08-09; if it lapsed, step 6 must be re-run to reissue it.
3. **What SES's `Authentication-Results` actually says on a real send.** §7's expectation is what
   alignment ought to produce, not a reading taken from a delivered message.
4. **Whether `dmarc@jobbliggaren.se` exists.** §4 assumes it can be created at STRATO; that has
   not been done, and an `rua=` pointing at a non-existent mailbox collects nothing while looking
   correct.
5. **Deliverability beyond authentication.** DKIM and DMARC decide whether a message is accepted,
   not whether it lands in an inbox. Reputation on a new sending domain is unmeasured and cannot
   be measured without volume this account is not permitted to send.
