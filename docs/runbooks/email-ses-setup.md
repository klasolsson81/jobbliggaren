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

> **THE DOMAIN IS VERIFIED AND CAN SIGN, AND MAIL HAS NOW BEEN SENT — TWICE, 2026-08-12, BOTH TO
> THE VERIFIED TEST ADDRESS.** §3 is a report; §7 is no longer a design.
>
> Both were accepted with a `MessageId`, through the production key and its scoped policy, and
> both landed in the **inbox** rather than the spam folder. The first arrived with every
> non-ASCII character double encoded (`Bekräfta` as `BekrÃ¤fta`) and the cause is worth carrying
> forward, because it will catch the next person testing from Windows: **the AWS CLI decoded the
> `file://` payload with the host ANSI code page**, not UTF-8. The template was never involved —
> its bytes and the test file's bytes were both measured correct UTF-8 (`c3 a4`) — and production
> cannot reach this failure at all, since `SesEmailSender` hands the SDK a .NET string with
> `Charset = "UTF-8"` and reads no file. **Write the CLI payload as pure ASCII with `\uXXXX`
> escapes**; a JSON parser then reconstructs the same string on any host.
>
> Klas published the three CNAME records at STRATO on **2026-08-10**. SES moved
> `DkimAttributes.Status` `PENDING` → **`SUCCESS`** and `VerifiedForSendingStatus` → **`true`** in
> under half an hour, against the 72 h AWS reserves for propagation. Measured independently against
> the SES API **and** against public DNS, never read out of STRATO's control panel — what counts is
> what SES can see. Verification rows **33 and 34** in
> [`vps-deploy-stack.md`](./vps-deploy-stack.md) §5 carry the values and the date; the measurement
> is protocolled on
> [#183](https://github.com/klasolsson81/jobbliggaren/issues/183#issuecomment-5240287056).
>
> **What does NOT follow from a delivered message, and is the whole reason this note survives
> rather than being deleted:** inbox placement is not an authentication reading. Row 37 stays
> **open** until `Authentication-Results` is read out of the received message — arriving is
> evidence about a filter's verdict, not about DKIM alignment under `p=reject`, and the two can
> disagree in both directions. The account is still in the **sandbox** (its quotas are in §2 and
> its recipient count in §3 step 6; neither is restated here), production access was applied for
> and **denied** on 2026-08-12 with the support case live (§8), and
> `Email:Provider` is still unset, so the product sends nothing. Being able to sign is not the
> flip; the flip is [`release-checklist.md`](./release-checklist.md) §2.5 and is Klas's alone.

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
nslookup -type=TXT _dmarc.jobbliggaren.se 8.8.8.8   # 2026-08-09: v=DMARC1;p=reject; (no rua=).
                                                    # §4 adds a rua= via row 38; p=reject is the invariant.
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

**Published 2026-08-10.** The prefix-only rule held: all three records resolve (step 5), so the
double-append failure mode this step warns about did not occur. That is an observation about one
publication at one registrar, not a reason to skip the check next time — the failure is silent for
up to 72 h, which is exactly why it is measured rather than assumed.

### Step 5 — verify the records resolve, against the outside

```bash
for t in wxsvjat5qoockft4lvfyimv734imai5w vwts4nge2rzcdl2zvqxw3wmofzyj3c5n 4s4biygclhewprir3bmlnvmxvhh2efc7; do
  nslookup -type=CNAME "${t}._domainkey.jobbliggaren.se" 8.8.8.8
done
# expect, for each: canonical name = <same token>.dkim.amazonses.com
```

Query a public resolver, not the registrar's. What matters is what SES can see, not what the
STRATO control panel believes it saved.

Ran 2026-08-10 against Google DoH: each of the three resolved to `<same token>.dkim.amazonses.com.`,
and the three tokens were checked against the identity's own — read out of the API response before
publication, not retyped from the table above.

Then let SES decide:

```bash
aws sesv2 get-email-identity --email-identity jobbliggaren.se \
  --profile jobbpilot --region eu-north-1 \
  --query '{Status:DkimAttributes.Status,Signing:DkimAttributes.SigningEnabled,
            Zone:DkimAttributes.SigningHostedZone,Verified:VerifiedForSendingStatus}'
# expect: SUCCESS / true / dkim.amazonses.com / true. AWS reserves up to 72 h for propagation.
```

Ran 2026-08-10: `Status SUCCESS`, `SigningEnabled true`, `SigningHostedZone` unchanged, and
`VerifiedForSendingStatus true` — in under half an hour, not the reserved 72 h. **SES is the only
authority on this question**, which is why the DNS check above does not close it: records that
resolve for us can still be records SES has not yet re-read. Protocolled in verification row 34.

*The projection names all four fields explicitly because they sit under two roots — three under
`DkimAttributes`, one at the top level. The narrower two-field projection this step carried until
2026-08-10 could not produce `SigningEnabled` or `SigningHostedZone`, which the prose beside it
nevertheless reported. Verification row 34 reads the same call unprojected, which is the authority;
this projection is the convenience.*

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

**Confirmed by 2026-08-10:** the account has **exactly one** verified recipient
(`klasolsson81@gmail.com`), measured as part of the domain measurement on
[#183](https://github.com/klasolsson81/jobbliggaren/issues/183#issuecomment-5240287056). The 24 h
link therefore did not lapse and this step does not need re-running.

**The sandbox is not a problem to be solved here.** It caps sending at 200/24 h and 1/s, which is
ample for one recipient, and it enforces at the account level what we want during dev anyway:
that nobody but Klas can receive mail from this system. Leaving it is §8.

---

## 4. DMARC aggregate reporting — and the address must be on this domain

The domain publishes `v=DMARC1;p=reject;` with no `rua=`, so mail that fails DMARC is rejected
and **no one is told**. Adding a reporting address does not change the policy; it is purely
additive.

⚠ **`_dmarc` is NOT a free TXT record here, and reaching for one is the destructive mistake.**
Measured 2026-08-10 while publishing the DKIM records: STRATO's own **DMARC control** was found
unset, saving it produced `STRATO Standard DMARC-regel`, and the published record was **unchanged**
afterwards. So that control is what already owns `v=DMARC1;p=reject;` — there is no separate TXT row
to edit. It is the button to press when `rua=` is added, and **it must not be set to "Ingen"**: that
deletes the `p=reject` Klas's ordinary mail path depends on, which is the exact risk verification
row 36 exists to control. Adding a second `_dmarc` TXT record beside the control would in any case
be invalid — RFC 7489 §6.6.3 discards a domain with multiple DMARC records outright.

Set the value through that control:

| STRATO DMARC control | Value |
|---|---|
| the existing `_dmarc` rule (do **not** create a new TXT record) | `v=DMARC1;p=reject;rua=mailto:dmarc@jobbliggaren.se` |

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

**Created 2026-08-12, and the precondition is discharged.** IAM user `jobbliggaren-ses`, policy
`jobbliggaren-ses-send`. Measured with the key itself, not the admin role:
`arn:aws:iam::710427215829:user/jobbliggaren-ses`, `Account 710427215829`.

**The narrow resource form does not work, and the reason is not obvious.** A policy whose
`Resource` was the domain identity alone — the shape "scoped to `ses:SendEmail` only" invites —
returns `AccessDenied` naming the **recipient**:

```
User `.../jobbliggaren-ses' is not authorized to perform `ses:SendEmail'
on resource `arn:aws:ses:eu-north-1:710427215829:identity/klasolsson81@gmail.com'
```

SES authorises the call against the recipient identity as well as the sender. So the live policy
(v2) uses `identity/*` with a `ses:FromAddress` condition on `no-reply@jobbliggaren.se`, and it is
**the condition, not the resource, that constrains the key**: it cannot send as any other address.
Read the resource wildcard as breadth over *recipients*, which is what sending requires.

`ses:SendRawEmail` is deliberately **absent**. `SesEmailSender` builds `Simple` content, so
production never needs it; a raw-MIME send attempted during testing was refused, which is the
policy behaving correctly rather than a gap to fill.

### 6.3 The flip

`Email:Provider=Ses` is not set by this runbook and never by CC. It is gated by
`release-checklist.md` §2.5. **Point 1 is not green; its legs and their statuses live in the
point itself**, and §2.5's own preamble instructs reading them there rather than from any
summary — including this one. Two mechanical prerequisites also have to exist first — the
`Email__*` variables set in `deploy/.env`, and the two credential files written on the box —
and §8's `Email__*` entry names both, along with the injection gap that owns the second.

---

## 7. Verifying it actually works

Verification rows 33–38 in [`vps-deploy-stack.md`](./vps-deploy-stack.md) §5 are the protocol.
**This section produces rows 35, 36 and 37 only** — rows 33 and 34 are produced by §3 step 5, and
row 38 by §4, whose own instrument cites it. Naming the split matters because a row produced in two
places is a row whose evidence can be ticked from whichever half ran.

**The control measurement comes first, and it is the point of §5.** After any DNS work, confirm
that the existing mail path is untouched:

```bash
nslookup -type=TXT strato-dkim-0002._domainkey.jobbliggaren.se 8.8.8.8   # unchanged
nslookup -type=TXT strato-dkim-0003._domainkey.jobbliggaren.se 8.8.8.8   # unchanged
nslookup -type=MX  jobbliggaren.se 8.8.8.8                               # expect: smtp.rzone.de
nslookup -type=TXT _dmarc.jobbliggaren.se 8.8.8.8   # expect: p=reject present (a rua= added by row 38
                                                    # is additive and expected), and EXACTLY ONE record
nslookup -type=TXT jobbliggaren.se 8.8.8.8                               # expect: still no TXT
```

The `_dmarc` line is the fifth leg, added 2026-08-10 with §4's mechanism. Read it for **count as well
as value**: two DMARC records are not a stricter policy but no policy at all (RFC 7489 §6.6.3
discards anything not starting with `v=DMARC1`, then terminates on a surviving set of more than
one), and that failure looks identical to a correct one in a panel that lists them.

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
  `deploy/docker-compose.yml`. `deploy/systemd/jobbliggaren-inject-secrets.sh` writes the two
  SES credential **files** the `_FILE` pointers name, and prompts for them when
  `EMAIL_PROVIDER=Ses`, when either pointer is set, **or** under `JBL_INJECT_SES=1` (#183). The
  injection gap this entry used to record is closed.
  **Inject before you edit, and the order is not cosmetic:** each of the first two conditions is
  itself a boot refusal while the files are absent, so setting the variable first takes the box
  down and the injection then runs under an outage. Run
  `sudo JBL_INJECT_SES=1 …/jobbliggaren-inject-secrets.sh` first, then set `EMAIL_PROVIDER=Ses`,
  the two pointers and `EMAIL_SES_REGION`, then restart. `--check` names any line still missing.
- **Leaving the SES sandbox — APPLIED FOR 2026-08-12, and the position this entry held until
  then did not survive contact with the application.** It read: AWS requires the applicant to
  *"confirm that you have a process in place for handling bounce and complaint notifications"*,
  no such process is built, so applying would attest to something untrue. What was actually
  attested names only the account-level suppression list — enabled for `BOUNCE` and `COMPLAINT`,
  measured 2026-08-12 — and states the volume it is proportionate to. It claims no
  application-side handling, because there is none. **The application was submitted before this
  entry was read**, which is the process failure worth recording: the runbook owned the
  decision and was consulted after it. `ReviewDetails.Status` went `PENDING` → **`DENIED`**
  within ten minutes, with AWS's correspondence asking for detail rather than closing the door;
  the reply is the live path. Read the status from `aws sesv2 get-account`, never from the
  support case — the Support API is unavailable on this account's Basic plan
  (`SubscriptionRequiredException`, measured 2026-08-12).
- **Bounce and complaint handling itself** — no owner in code today, and **the obligation does
  not come from AWS.** An earlier version of this line called it the thing to build *if AWS asks
  for more than the suppression list*, which put a GDPR duty behind a vendor's discretion.
  `security-auditor` rejected that framing on 2026-08-12 and the reasoning is short: a SES
  `Complaint` means the recipient marked the message as spam. For the notification mail, which
  runs on consent, that is an Art. 7(3) withdrawal and an Art. 21 objection arriving through a
  channel nothing in `src/` reads. **There are TWO consent pairs, not one**, and a feedback path
  that updated only the first would leave the second asserting live consent for the same
  objector: `NotificationConsentWithdrawnAt` (match notifications) and
  `FollowedCompanyNotificationConsentWithdrawnAt` (followed-company notifications), kept separate
  because collapsing them would be an Art. 7 granularity violation (ADR 0087 D5). Measured: both
  are written only by
  `JobSeeker`'s own opt-out methods. The suppression list stops delivery but never reaches the
  register, so the consent record would go on asserting live consent for someone who has
  objected. That is a defect whether or not AWS ever asks.
  **The path does not cost the ROPA leg.** The obvious mechanism — a configuration set with an
  SNS event destination — would, because the retention entry's first leg is that no
  `ConfigurationSetName` is in play. But SES v2 `SendEmail` carries
  `FeedbackForwardingEmailAddress` as a **per-request** parameter, and email feedback forwarding
  needs no configuration set at all. Build it that way —
  [#1323](https://github.com/klasolsson81/jobbliggaren/issues/1323) owns it, so this paragraph
  points at work with an owner rather than at nothing.

---

## 9. Unmeasured, and named

*Two entries left this list on 2026-08-10 — whether the three CNAMEs verify, and whether the
recipient identity was confirmed. Both are now measured, and a measured thing does not belong in a
section titled "Unmeasured": the outcomes live in §3 steps 5 and 6, beside the commands that
produced them. Everything below is still open, and none of it is discharged by the domain being
verified.*

1. **What SES's `Authentication-Results` actually says on a real send.** §7's expectation is what
   alignment ought to produce, not a reading taken from a delivered message. **This is the one the
   verification is most likely to be mistaken for:** a signing domain is a precondition for
   alignment, never evidence of it — `dkim=pass` on some other `header.d` would not survive
   `p=reject` and would look identical to anyone reading only for the word "pass". Row 37.
2. **Whether `dmarc@jobbliggaren.se` exists.** §4 assumes it can be created at STRATO; that has
   not been done, and an `rua=` pointing at a non-existent mailbox collects nothing while looking
   correct.
3. **Deliverability beyond authentication.** DKIM and DMARC decide whether a message is accepted,
   not whether it lands in an inbox. Reputation on a new sending domain is unmeasured and cannot
   be measured without volume this account is not permitted to send.
