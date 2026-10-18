# ADR 0166 — Legal basis for publishing imported job-ad text on an indexable page: the legitimate-interest assessment, the Art. 6(4) compatibility test and the Art. 35(11) screening

**Date:** 2026-10-11
**Status:** Accepted 2026-10-12 for D1–D11. The Amendment 2026-10-11 (Art. 9) is the controller's
override of a `security-auditor` Blocker and is not signed by her.
**Deciders:** Klas Olsson (controller; approved 2026-10-12) · `security-auditor` (signed 2026-10-12: D1–D11
and the notice text pinned as signature 3, not the Amendment 2026-10-11; her plan read of 2026-10-11 set the
four weights in D4) · `senior-cto-advisor` (ruled on 2026-10-11 that this assessment lives in an ADR of its
own and that the session may propose a conclusion and not declare one;
`docs/reviews/2026-10-11-2090-s2-plan-cto.md`, local)
**Related:** ADR 0160 (D4, D8, D9.1, D9.2, D9.3) · ADR 0165 (the page) · ADR 0106 (K3, D1, D5, Tier A and Tier B, A1) ·
ADR 0087 D8(c) · ADR 0090 and ADR 0101 (the precedents for a DPIA ruling written as an ADR) · DPIA #456 §8-B · ADR 0071 ·
ADR 0154 · [#2090](https://github.com/klasolsson81/jobbliggaren/issues/2090) ·
[#2091](https://github.com/klasolsson81/jobbliggaren/issues/2091) ·
[#2098](https://github.com/klasolsson81/jobbliggaren/issues/2098) ·
[#2123](https://github.com/klasolsson81/jobbliggaren/issues/2123) · #841
**Numbering:** written 2026-10-11 as ADR 0164 and renumbered 2026-10-12 to ADR 0166, because `origin/main` took 0164 for the notification-mail pauses (#2135) and 0167 for the minus-word search (#2145). ADR 0165 (the page) keeps its number.
**Measured against:** the branch `feat/seo-s2-public-job-page`, cut from `8bc108dcf`; `origin/main` at `11750988b`,
2026-10-11. Nothing in this ADR is a reading of live data except where a date and a source are given.

> **Provenance.** Written by `adr-keeper` for the driving session, from its brief (CLAUDE.md §9.2, §13). The weights in D4 are
> `security-auditor`'s, set at her plan read; the reasoning is the CTO's ruling and the facts in ADR 0106, ADR 0160 and the
> register. **The conclusion is proposed by the drafting session and is not the controller's until he approves it.** This ADR
> is the assessment that ADR 0106 listed as "LIA K3" (CC drafts, Klas signs). It was not found in the repository: the path the
> session log of 2026-07-17 names for it, `docs/research/2026-07-16-842-tier-a-lia.md`, does not exist.

---

## Context

ADR 0160 D9.2 makes a go-live precondition of S2 that "the legitimate-interest assessment, which must weigh that recruiter
names in free text become findable through a search engine, and the Art. 6(4) compatibility reasoning" are written, and that
the notice, the register row and the policy say what the page does. The page (ADR 0165) shows a Listed Platsbanken ad at
`https://jobbliggaren.se/jobb/<uuid>`, to anyone, indexable. It shows the title, the company name, the dates, the full
description and a link to the ad. It never shows the contact block, the organization number or the raw payload.

What the record said before this ADR:

- **The register** (`Behandling: JobTech-import (Platsbanken)`, local file) gives the basis as Art. 6(1)(f) because "annonsen är
  redan publicerad av Arbetsförmedlingen för allmän indexering". ADR 0106 K3 calls that "the one argument Google Spain forecloses
  outright" and asked for a balanced assessment instead. K3 was never written, and the amendment of 2026-07-26 records it
  "untouched".
- **The recruiter notice** (`recruiterNotice`, last updated 2026-07-16) says the interest is the user's: following up their own
  application. Once the page is live that is no longer the whole purpose, and the notice would be false (Art. 5(1)(a), 12(1)).
- **The facts that carry the balance.** About 22 % of the ads in a sample of 100 (the live JobTech API, 2026-07-13, ADR 0106
  A1) carry the contact only in the free text. Tier A scrubs e-mail addresses and phone numbers and never names: name
  recognition was refused (ADR 0106 D5) and an LLM is forbidden (ADR 0071). The remedy for a name is erasure of the whole ad
  (Tier B). A sole trader's organization number is their personnummer (#841), and a sole trader's company name can be their
  personal name (ADR 0087 D8(c); the register).

## Decision

**Proposed conclusion: publishing the Listed ads on `/jobb/<uuid>` may proceed on Art. 6(1)(f), with the safeguards in D5 and
with the residuals in D4 stated and carried, on three conditions. (1) The S2-P3 measurement reads 0 (D11). (2) The recruiter
notice, the privacy policy and the register row say what D7 lists. (3) Klas approves this ADR and `security-auditor` signs
it.** If `security-auditor`'s weighing comes out against publication, that is a stop for Klas and not something the session
settles.

The structure below is the three-part test (purpose, necessity, balancing), the safeguards, the Art. 6(4) factors, the
transparency duty and the Art. 35(11) screening.

### D1 — The purpose and whose interest it is

The interest pursued is that of the people who search for a job, and Jobbliggaren's own: that an open ad can be found by
someone who searches for the work or the employer, without knowing Platsbanken and without an account, and that Jobbliggaren
is a place such a search lands (epic #2090). It is not the recruiter's interest and not the advertiser's. The notice's
sentence "Intresset är användarens" is struck for that reason (D7).

### D2 — Necessity

- **The page needs the text.** A searcher looks for the words in the description, and S3's `JobPosting` markup needs the full
  description on the page (Google, job posting structured data, updated 2026-09-08, read 2026-10-11). A page of structured
  fields alone would not serve the purpose (Alternative B).
- **What was left out to meet necessity.** The contact block, the organization number, `RawPayload`, user state and match data
  (ADR 0160 D4). Ended and erased ads show nothing (one neutral 410, ADR 0160 D3). Only ads imported from the published source
  are public, never a manual row (ADR 0160 D2). No sitemap before S4. The metadata never uses free text (D4, W4).
- **What necessity does not reach.** The free text is published as stored. A second, public-only redaction at read time was
  rejected (ADR 0160 D4; ADR 0106 D4), and a name can not be found without name recognition (D4, W1).

### D3 — What this ADR does not rely on

- **"Already published by Arbetsförmedlingen"** is not a ground and is not used to refuse a request: we are an independent
  controller of our own copy (Google Spain, C-131/12, as ADR 0106 uses it). Tier B honours an Art. 17 request against our copy
  whatever Arbetsförmedlingen does.
- **CC0** waives copyright and database rights. It does not discharge GDPR (ADR 0160 D8).
- **The advertiser's consent.** Arbetsförmedlingen's advertiser terms (valid from 2025-12-01) require the advertiser to hold
  the contact persons' consent to publish them. That consent is the advertiser's. Jobbliggaren has none and does not treat the
  terms as giving it one.

### D4 — The balancing: four weights that `security-auditor` requires, and what each leaves

**W1 — A name in running text becomes findable by name through a search engine.**
Tier A can not reach it (D5 of ADR 0106; ADR 0071). The people are not our users, have no relationship with us and did not
choose that we publish. *What is done:* Tier B erases the whole ad on request and the page then answers the neutral 410; the
objection under Art. 21 is met in full; the runbook asks search engines to drop the address (step 2.4a). *What is left:* the
person has to learn of the page and ask, the notice is a public notice and not a message to each person (ADR 0106 K4 says not
to e-mail recruiters), and the time from publication to a request is a window in which the name is indexed.

**W2 — A sole trader's company name is a personal name, and it is indexed.**
`company_name` is in the page title, the company line and the link preview. The organization number is never published
(ADR 0087 D8(c)). The register treats the name as public metadata because Arbetsförmedlingen published it. *What is done:*
Tier B applies to a sole trader as to anyone; the S2-P3 count covers `company_name` and `url` (D11). *What is left:* the name is
findable by search, and a preview of title and company can sit in a cache we can not reach (W4).

**W3 — A personnummer in the text: the #2123 residue, in new imports too.**
#2098 masks a personnummer in the title and the description at import (a valid date and a passing check digit, length-preserving
mask; ADR 0106, Amendment 2026-10-11). #2123 (open) records the forms the scanner does not mask: spaces around the hyphen,
and `.`, `/` or `_` as the separator. A listed ad is re-imported by the nightly snapshot sync, which runs the redaction on each
record, so a listed ad is masked at its next sync for the forms #2098 covers. The de-listed tail is not backfilled (Klas,
2026-10-11, ADR 0160 amendment). **This is not a weight to balance.** A personnummer on a public, indexed page does not meet
dataskyddslagen 3 kap. 10 § and is Blocker-class (ADR 0160 D9.1). It is a gate (D11). *What is left, and it is stated here
without being measured:* S2-P3 is a reading at one date. After go-live each import can carry a form #2123 does not mask, and
nothing in this ADR measures that continuously. Closing it is #2123's fix.

**W4 — Copies out of reach.**
Search-engine caches, link previews and web archives hold copies that an erasure on our side does not remove. *What is done:*
the 410 and the Search Console and Bing steps in the runbook (Art. 17(2): reasonable steps toward those who hold the address),
`no-store` on our side so an erased ad is never served stale, and metadata built from title, company and dates only, so a
preview carries no free text. *What is left:* what is in those copies, and for how long.

**Beside the four.** The ad is published by the employer, in a source whose terms tell advertisers that its data may be shown
at other actors, and the person named is, in the usual case, named in a role to be contacted about a job. Those facts speak
for a reasonable expectation of display. They do not speak for an expectation of being found by name in a general search
engine, and the proposal does not rest on them (D6, factor b).

**Proposed weighing.** With the safeguards in D5, the objection and erasure paths working as described, and W3 gated by D11,
the interests of the job-seekers and of Jobbliggaren are not overridden by W1, W2 and W4, which are carried as residuals.
This is the proposal that Klas approves or does not.

### D5 — Safeguards

- **At import:** Tier A scrub of e-mail and phone from the text, with a promotion into the typed contact carrier that is never
  published (ADR 0106 D1, A1); the personnummer mask on title and description (#2098). Ads are public only if imported from the
  published source (ADR 0160 D2).
- **On request:** Tier B whole-ad erasure, with the neutral 410 that an ended ad also gets, so an erasure is not broadcast
  (ADR 0160 D3). Art. 21 objections are met in full and no balancing is made against the person (the notice says so).
- **At the page:** no contact block, no organization number, no `RawPayload`; the public DTO's member set is pinned by a
  contract test and a wire test (ADR 0160 D4); the description is plain text, never HTML; metadata from structured fields only;
  `Cache-Control: no-store`; the per-IP rate limit (60 per 60 s, ADR 0160 D5); no log line with title, company or description.
- **Transparency:** the recruiter notice, linked from every public page (D7).
- **After an erasure:** runbook `recruiter-pii-erasure.md` step 2.4a, the Art. 17(2) step: the address is built from the
  confirmed ad ids (our uuid), the 410 is verified anonymously first, then Search Console Removals and Bing, the date is kept in
  the case file and not in the repository, and the reply says what can not be reached.

### D6 — The Art. 6(4) compatibility test, five factors

ADR 0160 D9.2 requires this reasoning. It is written between the purpose for which Arbetsförmedlingen publishes an ad and the
purpose of showing it on an indexable page. Whether Art. 6(4) applies in terms to data taken from another controller's open
dataset, or the five factors only inform the balance under Art. 6(1)(f), is not decided here. The factors are reasoned either
way, which is why they sit beside D4 and do not replace it.

- **(a) The link between the purposes.** Arbetsförmedlingen publishes an ad so that people who might apply can find it. The
  page shows the same ad so that people can find it through a search. The aim is the same; the channel and the audience differ
  (a general search engine, the public, not users of the open interface).
- **(b) The context, and the relationship.** The people named are named by their employer in an ad on a public source. They
  have no relationship with Jobbliggaren. Arbetsförmedlingen's advertiser terms (valid from 2025-12-01) tell advertisers that
  personal data in a published ad may be shown at other actors who fetch ads through the open interface, and require the
  advertiser to hold the contact persons' consent. Those terms support an expectation of display at other actors. They do not
  decide the expectation of being indexed by a general search engine (D4, beside the four). Whether the same ads are already
  indexed at Arbetsförmedlingen was not measured by the drafting session.
- **(c) The nature of the data.** Names, roles and whatever the advertiser wrote; e-mail and phone are scrubbed at import and
  a personnummer is masked. No special-category or criminal-offence field is in the DTO. The free text is not classified, and its
  content in that respect is not measured.
- **(d) The possible consequences.** Findability by name for as long as the ad is listed and in caches after it, the risk of
  unwanted contact, and for a sole trader their name. Reversible on our side by Tier B and the 410, and partly on the search
  engines' side by the removal steps. Not reversible in a copy someone else made.
- **(e) The safeguards.** D5. Encryption or pseudonymisation is not available: the purpose is public display.

**Proposed finding:** the display is justified under the assessment in D4, provided the safeguards in D5 work as described and
the gate in D11 reads 0.

### D7 — Transparency: Art. 14(2)(b), Art. 12 and Art. 14(5)(b)

The data is not obtained from the person, so Art. 14 applies, and the legitimate interests pursued are to be stated
(Art. 14(2)(b)). Individual notice rests on the disproportionate-effort exemption in Art. 14(5)(b), which requires that the
information is made publicly available (ADR 0106 K4: the notice is the price of the exemption, and recruiters are not e-mailed).
The notice text lives in the messages `recruiterNotice` (sv and en), not here, and `security-auditor` signs it. The S2 PR revises
it to say, at least:

1. the purpose: display on a public page that search engines may index for as long as the ad is open;
2. that a contact block is never shown, and that a name in running text can be;
3. that the interest is the job-seekers' and Jobbliggaren's own (the sentence "Intresset är användarens" is struck);
4. the recipients: the public, and search engines;
5. the retention: the public page answers "not available" once the ad closes, and the sentence that contact details are erased
   when the ad leaves search does not extend to a name in the text;
6. that on erasure we ask search engines to remove the address (only because step 2.4a is mandatory).

The privacy policy's paragraph "Kontaktuppgifter i jobbannonser" gets one sentence to the same effect, and the line in
`content-legal.json` that says the service's own interfaces are for signed-in users is corrected. The register row, local file,
is amended to the purpose and basis above and points here. Art. 12(1): the text is concise and in plain language.

### D8 — Art. 35(11): the screening of DPIA #456 §8-B against publication

DPIA #456 (ADR 0090) holds the recruiter-PII risk register at §8-B (R-842-1 to R-842-7) and the Art. 19 recipients at B8. Art.
35(11) asks for a review of whether processing is performed in accordance with the assessment at least when the risk it
represents changes. Reading §8-B against the page, the screening finds:

1. **No new data category and no new field.** The public DTO is a subset of what the signed-in detail already shows. What
   changes is the audience. (Art. 9 in the free text was raised at review and is decided in the Amendment of 2026-10-11.)
2. **A premise of the assessment stops being true.** B8 says there are "no external recipients at all: no external search
   index". From go-live search engines, link-preview fetchers and archives are recipients, each an independent controller for
   its own processing.
3. **Ratings that depend on the audience were rated for signed-in searchers,** and the DPIA's text records no new rating for a
   public one:
   R-842-1 (contact details exposed to full-text search; residual Low × High after Tier A), R-842-3 (a name is searchable and no
   pattern reaches it; Low × High, Tier B only) and R-842-4 (obfuscated or image-embedded contact details; a permanent residual).
4. **R-842-6 (the backup tail) is unchanged,** and STOPP-4 is still open (ADR 0106, Amendment 2026-07-26).
5. **DPIA #456 is not signed for the recruiter-PII processing** (STOPP-3 and STOPP-4 are open). There is no signed
   assessment against which the review could confirm that the processing conforms.

So the change of risk that Art. 35(11) names is there (2, 3). Whether the answer is an addendum to §8-B that takes R-842-1,
R-842-3 and R-842-4 again for a public audience and amends B8, or a new full DPIA under Art. 35(1), is **Klas's decision as
controller, on `security-auditor`'s recommendation.** This ADR does not make it. His approval below records his answer.

### D9 — The licence

The dataset "Platsannonser" is CC0 1.0 (Sveriges dataportal; Arbetsförmedlingen's catalogue), read 2026-10-10 (ADR 0160 D8).
It permits reuse of the text and is not a legal basis for personal data in it (D3).

### D10 — Lapse triggers

This ADR is the single home of the list, and Klas is the named human reader. Nothing detects a trigger automatically: each is
checked by reading this list when the event is proposed or reported. A fired trigger withdraws the conclusion for the widened or
affected scope only, until Klas has taken it again. It withdraws no finding and none of D4's residuals.

1. **Any widening of the public surface:** the S4 sitemap, the S5 catalogue, a second source, an employer profile, or any new
   public page that carries ad text.
2. **A measured personnummer in a listed ad** (a validated count above 0 at any later reading).
3. **A recruiter complaint** about the page.
4. **An Art. 36 contact** (a prior-consultation contact with the supervisory authority, whether we start it or it does).

### D11 — The S2-P3 gate, and where it stands

S2-P3 is a hard gate before go-live and is not waived (Klas waived only the backfill, ADR 0160 amendment). It is two counts
over listed `job_ads` (Active, `expires_at` null or not past, imported from Platsbanken), in the title, the description,
`company_name` **and** `url` (the four values the page prints). The first is a superset pattern over both families of forms: #2098's plain and 12-digit forms and #2123's
spaced-hyphen and `.`, `/`, `_` forms. The second is a date- and check-digit-validated count in SQL, by the same predicate as
`Personnummer.TryParse`. **The gate is the validated count: 0.** The SQL is written so that no cast can fail on a data value
(a cast error echoes the value, which would be an unintended read, CLAUDE.md §9.2 (e)), and only counts are written down. It is
taken after the first nightly snapshot sync since #2120 was deployed, so the earliest reading is the morning of 2026-10-12. The
date, the time and the two counts are recorded on #2090 and in this ADR.

**Readings (counts only).** 2026-10-12 03:54-03:55 UTC, release 1627: listed 39 390, shaped 177, validated 0. 2026-10-12 04:27 UTC, release 1628 (`9ca862013`; `--status` read consistent before and after): listed 39 409, shaped 177, validated 0 in the title, the description, the company name and the link; link host other than Arbetsförmedlingen 0. Reading 3, 2026-10-12 05:15 UTC, release 1628, `--status` read consistent before and after: listed 39 415, shaped 177, validated 0 in the title, the description, the company name and the link; link host other than Arbetsförmedlingen 0. The gate reads 0.

## Alternatives considered

B and C are the counterfactuals the necessity test (D2) needs. They are stated for Klas to confirm, add to or reject when he
approves.

### Alt A — Publish on Art. 6(1)(f) with these safeguards (proposed)
**For:** serves the purpose in D1; keeps the contact block, the organization number and the raw payload out; every remedy
(Tier B, Art. 21, the 410, the removal steps) exists and is tested.
**Against:** carries W1, W2 and W4 as residuals and W3 until #2123 is fixed; depends on the notice being found.

### Alt B — Publish structured fields only, without the description
**For:** removes W1 and shrinks W3, because the free text is where a name or a personnummer is.
**Against:** S3's `JobPosting` markup needs the full description on the page, and a page of fields alone would not serve a
searcher (D2). Not chosen.

### Alt C — Do not publish; keep the ads signed-in only
**For:** no new exposure.
**Against:** the epic's purpose is not met.

### Alt D — Rely on "already published by Arbetsförmedlingen"
**For:** the register's old wording.
**Against:** ADR 0106 K3 calls it the one argument Google Spain forecloses, and it is the opposite of a balanced assessment.
Rejected (D3).

## Consequences

### Positive
- The legitimate-interest assessment exists, with the weights that were missing, before the page goes live, and ADR 0160 D9.2
  has something to point to.
- The notice, the policy and the register can say the same true thing.
- The Art. 35(11) question is put to the controller in the open, with what it rests on.

### Negative and risks
- **The residuals W1, W2, W4** are accepted by the proposal, not removed. A person can still be findable by name for a while.
- **W3 is unmeasured** until S2-P3, and after go-live it is measured only when someone measures it.
- **An unsigned DPIA** sits under a processing whose audience changes (D8).
- **The assessment is as good as its facts.** The 22 % is a sample of 100 ads from 2026-07-13. Whether the ads are already
  indexed at Arbetsförmedlingen and what the free text holds in Art. 9 terms were not measured.

## Implementation

Ships under #2090 S2 with ADR 0165 and the three amendments (ADR 0053, 0017, 0160).

- `messages/{sv,en}/content-legal.json`: the `recruiterNotice` revision (D7), the privacy-policy sentence, the corrected line.
- `docs/runbooks/recruiter-pii-erasure.md`: step 2.4a (in the S2 PR).
- `docs/runbooks/gdpr-processing-register.md` (local, ADR 0072): the JobTech row's purpose and basis, pointing here. Edited in the
  main copy before `agents-done`.
- The S2-P3 reading, recorded on #2090 and entered in D11.
- Whether reply template B1 may carry one sentence that we have asked the search engines to remove the address is Klas's
  decision, and is taken only once step 2.4a is done.

## Sign-off

**Controller approval:** Klas Olsson, 2026-10-12, in the session's chat: the conclusion in D4 and D6 is approved. D8: an addendum to DPIA #456 §8-B, not a new full DPIA (written 2026-10-12, its text approved by Klas 2026-10-12 in reply to an explicit question, appended to the DPIA, local). This approval closes ADR 0106 K3.
**Search-engine properties:** Klas confirmed in the same chat that Google Search Console and Bing Webmaster Tools are both verified; he gave no date, and asked that it be recorded so. Measured 2026-10-12, public data only: `jobbliggaren.se` carries a `google-site-verification` DNS TXT record, which is a domain property; no Bing meta tag or `BingSiteAuth.xml` is served, so Bing's route is not measured.
**Signature:** `security-auditor`, 2026-10-12: signs D1–D11 and the revised `recruiterNotice` and
privacy-policy paragraph (sv and en) as pinned as signature 3 by `content-legal-signed-copy.test.ts`.
It does not cover the Amendment 2026-10-11: that Blocker stands with its grade, overridden by the
controller. The signature is given for a merge on 2026-10-12; it lapses with any D10 trigger or a
trigger of that Amendment, and does not carry over to a later change of the signed text.

## Amendment 2026-10-11 — Art. 9 in the free text: the controller's decision (Klas)

**The finding (kept, with its grade).** At the review of PR #2131 `security-auditor` raised a GDPR **Blocker**: ads for personal
assistance can describe the person who is to receive the assistance (a first name, an age, a place, a diagnosis), which is health
data about a third party; Art. 6(1)(f) is not a ground for it and she saw no Art. 9(2) ground (9(2)(e) needs the data subject to
have made it manifestly public, and the advertiser is often an assistance provider or a municipality). Her recommendation was to
exclude at least the occupation group Personliga assistenter from the single publication predicate (#2133).

**What is known and what is not.** Nothing here is a count of named persons. The only reading is vocabulary, counts only
(CLAUDE.md §9.2 (a)), box read 2026-10-11 about 11:50 UTC: 41 548 listed ads; Personliga assistenter `sq3e_WVv_Fjd` 2 048
(887 with a title of the form "assistent till/åt", 1 385 with a health word in the text, 25 in the title); Elevassistenter
`XW9L_tA9_WaX` 208 (52; 95); Behandlingsassistenter och socialpedagoger m.fl. `BWwk_fYX_S5B` 382 (23; 170); outside those groups
246 such titles and 136 titles with a health word. These are upper bounds on vocabulary. **How many ads name a person, and how
many of those carry health data about them, is not measured, by her or by the drafting session.**

**The decision.** Klas, as controller, on 2026-10-11 (in the session's chat, in reply to the escalation): the ads are public at
Arbetsförmedlingen, so **no occupation group is excluded**; the page shows every listed ad as published, and no further
exclusion is built until someone asks for a change. #2133 is closed unbuilt. Its
design stays the remedy if a trigger below fires: a named set of groups next to `PublishedSource` in `JobAdPublication`, so one
predicate serves every consumer.

**What this is, and is not.** It is the controller's override of a `security-auditor` GDPR Blocker (Art. 5(2) record). It is not
an accepted-risk route under CLAUDE.md §9.6: the bearers are third parties, so the bound in route (3) does not hold. It
**withdraws the remedy only**: the finding and its grade stand, `security-auditor` has not signed this part and does not by this
amendment, and an accepted risk does not become measured by being accepted. D6(c) and D8 item 1 are read with this amendment.

**Triggers that reopen it (home: this ADR and #2090; reader: Klas):** (1) any request or complaint, from a person or an
authority, that names an ad in these groups or says it holds health data (the single ad leaves through Tier B and the 410, ADR
0160 D9.3, and the exclusion in #2133 is built); (2) a counts-only measurement of named persons or ages in the text, if Klas
asks for one, that finds a non-trivial number; (3) the public copy being served to a bearer of a kind none of the
three bearer-absence readings covers (a new register of accounts, sends or readers).

## References

- ADR 0160 D4, D8, D9 · ADR 0165 · ADR 0106 (K3, K4, D1, D4, D5, A1, the amendments of 2026-07-26, 2026-10-10 and 2026-10-11) ·
  ADR 0087 D8(c) · ADR 0090 (DPIA #456) · ADR 0101 · ADR 0071 · ADR 0154
- DPIA #456, `docs/research/2026-07-03-dpia-456-historik-profilering-scb.md`, §B3 (risk register) and §B8 (recipients) (local)
- `docs/runbooks/recruiter-pii-erasure.md` §2.3, §2.4, §2.4a · `docs/runbooks/gdpr-processing-register.md` (local)
- `docs/reviews/2026-10-11-2090-s2-plan-cto.md` and the `security-auditor` plan read (`docs/reviews/2026-10-11-2090-s2-plan-*.md`,
  local) · `docs/reviews/2026-07-13-842-erasure-contract-cto-rebind.md` (K3 and K4 as first written, local)
- The GDPR articles as cited: Art. 5(1)(a), (c), 5(2), 6(1)(f), 6(4), 12, 14(2)(b), 14(5)(b), 17(1), 17(2), 19, 21, 35(1), 35(11),
  36; dataskyddslagen 3 kap. 10 §; Google Spain, C-131/12
- Google Search Central, job posting structured data (updated 2026-09-08, read 2026-10-11):
  https://developers.google.com/search/docs/appearance/structured-data/job-posting
- #2090 · #2091 · #2098 · #2123 · #841

## Amendment 2026-10-17 (#2090 S3, ADR 0171) — the record is made true (Art. 5(2)); approved by Klas 2026-10-17, signed by `security-auditor` 2026-10-17

D1–D11 and the earlier amendment stand and nothing above is rewritten. S3 puts a `JobPosting` data block on the listed `/jobb/<uuid>` page (ADR 0171). The block is built
from the anonymous read the page already makes, with the description HTML-escaped and a pinned key set (ADR 0171 D4). This amendment makes the record say what the page does.
It is made on `security-auditor`'s M2 of her plan read, `docs/reviews/2026-10-17-2090-s3-plan-security-auditor.md` (local).

**(i) D5 and W4: what "metadata from structured fields only" covers.** D5's "metadata from structured fields only" and W4's "a preview carries no free text" cover link
previews (OpenGraph and meta tags). The `JobPosting` data block carries the full description to search engines by design, as D2 foresaw when it rested necessity on S3's
markup.

**(ii) W4, what is left.** A copy shown in a search engine's job feature, removed by the 410 and by `validThrough`. Google states that the jobs experience can lag
("Users can still access the page through the jobs experience on Google even though the job has expired", Google Search Central, job posting structured data, page dated
2026-09-08, read 2026-10-17).

**(iii) D10 gains three triggers,** so that the list keeps its single home. They continue the numbering of D10:

5. Any **push** of a posting URL or its content to a third party: the Indexing API `URL_UPDATED`, an XML or job feed to an aggregator, or a Google partner submission.
6. A `JobPosting` on **any page other than** the listed `/jobb/<uuid>`.
7. A member **outside the pinned key set** of ADR 0171 D4, or any value the anonymous read does not carry.

**(iv) D7's recipients line stands.** "The public, and search engines" covers Google for Jobs and Bing in their job features; "andra" in the notice covers aggregator copies.
The `recruiterNotice` text is **not** changed (Klas's choice, 2026-10-17). How many aggregators ingest the markup is **not measured**.

**Sign-off**

**Controller approval:** Klas Olsson, 2026-10-17, in the session's chat, to `security-auditor`'s escalation relayed verbatim: "Ja, ingen trigger (Rek.)" (trigger 1 does not
fire for S3) and "Ja, godkänt (Rek.)" (this amendment).
**Signature:** `security-auditor`, 2026-10-17, on her scoped re-check of PR [#2160](https://github.com/klasolsson81/jobbliggaren/pull/2160) at head `144fda24c`: signs this amendment, points (i)–(iv) and D10 triggers 5–7, read with ADR 0171 D4 as delivered — the description HTML escaped per text run before any tag is wrapped and emitted only in the tag set `p`, `ul`, `li`, `br`; `hiringOrganization` as `{@type, name}` only; `jobLocation.address` as `addressLocality`, `addressRegion` and `addressCountry` only; no `url`. It does not re-sign D1–D11, which her signature of 2026-10-12 covers, and it does not cover the Amendment 2026-10-11, which she has not signed. It lapses with any D10 trigger, 5–7 included, and with any change to this amendment's text or to ADR 0171 D4's key set or escaping; the edit that writes this line and marks the amendment Accepted does not reopen it.

ADR 0166 remains **Accepted** for D1–D11; this amendment is **Accepted** (approved by Klas and signed by `security-auditor`, both 2026-10-17).

## Amendment 2026-10-18 (#2090 S4, ADR 0174) — the D10 re-take for the job sitemap; approved by Klas 2026-10-18, Proposed until signed by `security-auditor`

D1–D11 and the earlier amendments stand and nothing above is rewritten. D10's trigger 1 names "the S4 sitemap", so it fires for S4 by its own text and there is nothing to interpret. The
conclusion is withdrawn **for the sitemap only**: the public list of every listed address, and the systematic discovery it produces. The page keeps its conclusion. The signatures of
`security-auditor` of 2026-10-12 and 2026-10-17 lapse "with any D10 trigger", by their own words; read with D10's scope rule, that lapse withdraws nothing from the page. Klas took the
conclusion again for the sitemap on 2026-10-18 (sign-off below). This amendment is the single home of the legal outcome for the sitemap: ADR 0174 D8 points here and does not restate it.
It follows the order of `security-auditor`'s section 1, and her M1–M3 and m1–m4, in her plan read of 2026-10-18 (`docs/reviews/2026-10-18-2090-s4-plan-security-auditor.md`, local). The merge
waits for her signature. Without it the processing would rest on a withdrawn conclusion, and the notice's "Vi har vägt de intressena mot dina rättigheter" would be untrue for it
(Art. 5(1)(a), 12(1), 14(2)(b)).

**(i) D1: unchanged,** and applied to the sitemap as written. The purpose is the same.

**(ii) D2, necessity: re-taken.** D2 listed "No sitemap before S4" among the measures that met necessity. It is replaced and not deleted. The replacement is the sitemap's own minimisation:
`loc` only; membership from `JobAdPublication.IsListed` only; no time of any kind; pull only (ADR 0174 D2, D3, D8).
- *Why a sitemap is needed.* No public, crawlable page links to a job page before S5. `security-auditor` grepped `src/` on 2026-10-18: outside the signed-in app the only job link is the
  `/gast` demo's `/gast/jobb/…`, and `robots.txt` disallows it. Without a sitemap, D1 is not met at scale.
- *Alternatives.* No sitemap: the purpose is not met. A sitemap of recent ads only: it would confine W5 to early erasures, but older open ads would have no way in until S5, and its effect
  on search is not measured; she does not require it. The Indexing API: more intrusive, and it fires trigger 5.

**(iii) D4: W1, W2 and W4 are re-weighed at full scale, and W5 is new.** With a sitemap, being findable by name is the expected case within days, and no longer an incidental one. W3 is
handled by (ix).
- *W1 and W2.* A name in running text, and a sole trader's company name, become searchable at scale and quickly. Arbetsförmedlingen's own `robots.txt`, read 2026-10-18, admits crawlers to
  `/platsbanken/annonser/` except two named bots and names no ad-level sitemap, so our copy may become the more discoverable copy of a recruiter's name. Whether Google indexes individual
  Platsbanken ads is **not measured**, and neither is how large a share of the listed ads Google will index.
- *W4.* The same text, unscrubbed, is available in bulk through Arbetsförmedlingen's open API, so the list adds little to bulk harvesting. A copy in a cache or an archive stays out of reach.
- **W5 — The erasure inference, at scale.** ADR 0160 D3 recorded a measured limitation, not an acceptance and not a graded finding, and it stays one. What S4 changes:
  1. enumeration of the listed ads becomes complete and free;
  2. an erased ad's drop from the list is visible by comparing two reads taken minutes apart;
  3. anyone who archives the sitemap can make the comparison later, against Arbetsförmedlingen's open historical dataset, and not only against its live API before the ad's deadline;
  4. ads erased by one request drop out in the same tick, and the name they share points at the person who asked.

  What does not change: the list cannot tell an ended ad from an erased one. Our list follows Arbetsförmedlingen's own removals within the 10-minute stream, so only an early drop,
  relative to Arbetsförmedlingen, carries the signal. Joining our id to Arbetsförmedlingen's ad still needs one page read while the ad is listed: the link to Arbetsförmedlingen is on the
  page and never in the sitemap, the wire or the `JobPosting`. Matching on the ad text joins them too, so the join is no protection to rely on. The design changes that would reduce the
  inference are worse. Keeping an erased URL listed contradicts Art. 17(2) and ADR 0160 D6, and updating the list less often gains nothing, because an erasure usually comes days before
  the deadline. No design change is required.

  *What is done:* `loc` only; no time of any kind; ids in id order; no Arbetsförmedlingen id anywhere; the identical 410; the drop within the hard bound.
  *What is left:* the inference, for an observer who joins and compares.

**(iv) D5 gains S4's safeguards:**
- the minimisation in (ii);
- the hard 15-minute bound: the API judges the snapshot's age when it is read and refuses an older one, a failed tick never re-stamps it, the web caches nothing, every answer is `no-store`,
  and there is no CDN (ADR 0050, Amendment 2026-08-04);
- the new rate limit, `public-job-ad-listed-ids`, 6 per 60 s per client address, approved by `security-auditor` on 2026-10-18;
- the runbook step, `docs/runbooks/recruiter-pii-erasure.md` §2.4a, step 2: it checks that an erased address has left the sitemap, and a 503 never counts as a reading.

**(v) D6(d), the possible consequences, is updated** to the expected case and to W5. The window in which an erased address can still be listed is at most 15 minutes, and the page answers
410 from the moment of erasure. The list carries an address and not the ad, so a crawler that follows it in that window gets the 410. Art. 17(1) is met by `JobAd.Erase`, and Art. 17(2) by
the drop within the bound and §2.4a. What is left is W5.

**(vi) D7: no change.** The notice ("sökmotorer kan indexera den … kan hittas via en sökmotor"; "Mottagare är därför allmänheten och sökmotorerna") and the privacy-policy sentence stay true,
and the `recruiterNotice` is not changed. Its sentence "Vi har vägt de intressena mot dina rättigheter" is true for the sitemap from Klas's approval below, so the merge cannot come before it.

**(vii) D8: a second addendum to DPIA #456 §8-B,** and not a new full DPIA (Klas, answer 2). The addendum of 2026-10-12 rated R-842-3 Med × High on the assumption that discovery is
incidental, and R-842-4 High × High as the residual, and it recorded no determination under Art. 36(1). The second addendum is a local file, written in the main copy by the driving session
on these terms:
1. R-842-3's likelihood is taken again for systematic discovery (`security-auditor`'s reading: High).
2. R-842-9, the W5 inference, is added at Low × Med.
3. R-842-8's stock is taken from the reading in (ix).
4. The Art. 36(1) determination: **this is not a high risk in that sense, so no prior consultation with IMY is owed.** The reasons are that these are professional-role contact details,
   published by the employer, who under Arbetsförmedlingen's terms must hold the contact person's consent (that consent is not our legal basis, D3); that the details are open at the
   source; that Art. 21 is met in full; and that Tier B, the 410 and the removal requests are in place. On those reasons the severity of R-842-3 and R-842-4 is re-taken to Med. Had Klas
   kept High × High, prior consultation would have been owed before S4 went live, and the question would already have applied to the public page.
5. The Art. 9 row is untouched.

**(viii) D10.** Trigger 1 fired for the S4 sitemap, and Klas took the conclusion again for the sitemap on 2026-10-18. This withdraws no finding and none of D4's residuals. Two triggers are
added, continuing the numbering after the amendment of 2026-10-17 (triggers 5–7):

8. A sitemap entry with any element besides `loc`, or a `loc` that is not the page's canonical.
9. Membership of the sitemap from anything other than `JobAdPublication.IsListed`, or a list answer served older than 15 minutes: the bound raised, made configurable, or a cache put in
   front of it.

**Trigger 5 is not fired by S4** (Klas, answer 3). A sitemap that crawlers pull is publication and not a push "to a third party": we send nothing to anyone. Klas's manual submission of the
index address in Search Console, or in Bing's sitemap tool, is also outside the trigger. It carries no posting URL and no content, it goes to recipients that D7 already names, and it
repeats what `robots.txt` already says. Inside the trigger stay Bing URL Submission, IndexNow, the Indexing API, the Search Console API and any automated ping. Pull only also keeps this the
search engines' own collection and not a transmission by us (Chapter V); a push would need its own Chapter V reading.

**(ix) D11: the merge-day reading (W3).** D4 calls W3 a gate, ADR 0160 D9.1 puts a personnummer on the public page in the Blocker class, and the sitemap makes the whole listed stock a
crawl target. The last reading was 2026-10-12 05:15 UTC (39 415 listed, validated 0). S4 takes a fresh S2-P3 reading on the day of the merge: the same query, counts only, before
`agents-done`. The validated count must be 0, and it is recorded in two places: here, and in the record table of `docs/runbooks/public-job-page-publication-count.md`. A count above 0 fires
trigger 2, and S4 stops.

**The merge-day reading (counts only).** 2026-10-18 01:27 UTC, release 1636 (`d7b242b12`), `--status` consistent, the same query blob as the earlier readings (`8c02e37`): listed 42 223, shaped
231 (all in the description), **validated 0** in the title, the description, the company name and the link; link host other than Arbetsförmedlingen 0. The gate reads 0. If the merge slips
past that UTC day, the reading is taken again before it.

**Sign-off**

**Controller approval:** Klas Olsson, 2026-10-18, in the session's chat, to `security-auditor`'s escalation (`docs/reviews/2026-10-18-2090-s4-plan-security-auditor.md`, relayed verbatim by
the driving session through AskUserQuestion). His four answers, quoted without edits:
1. "Ja (Rek.)": the re-take of this ADR's conclusion for the sitemap, as (i)–(ix) list it.
2. "Ja, andra tillägget (Rek.)": a second addendum to DPIA #456 §8-B and not a new full DPIA, on the Art. 36(1) determination and the ratings in (vii).
3. "Ja, ingen trigger 5 (Rek.)": S4 does not fire trigger 5, on the reading in (viii).
4. "Ja, behåll (Rek.)": outside S4, the signed-in page keeps showing an ended ad while an erased ad answers 410. It is recorded in the ADR 0106 amendment of the same date and does not
   decide whether S4 can merge.

**Signature:** PENDING — to be entered when `security-auditor` signs, at her scoped re-check of the S4 PR's final head, read with ADR 0174 D1–D5 as delivered. It lapses with any change to
D1–D5 and with any D10 trigger, 8 and 9 included. It renews, for the page scope, her signature of 2026-10-12 (D1–D11 and signature 3; the notice is unchanged) and her signature of
2026-10-17 (the S3 amendment). It does not cover the Amendment 2026-10-11 (Art. 9), which she has not signed and which remains the controller's override.

ADR 0166 remains **Accepted** for D1–D11 for the page. This amendment is **Proposed** until the signature above is entered; Klas approved it on 2026-10-18. The edit that enters the
signature and marks the amendment Accepted does not reopen it.
