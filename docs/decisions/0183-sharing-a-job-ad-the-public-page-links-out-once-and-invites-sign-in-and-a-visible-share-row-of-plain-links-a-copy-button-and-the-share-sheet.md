# ADR 0183 — Sharing a job ad: the public page links out once and invites the visitor to sign in, and a visible share row of four plain links, a copy button and the share sheet

**Date:** 2026-10-24
**Status:** Accepted 2026-10-24 (the CTO's rulings under CLAUDE.md §9.2, as changed by Klas's override and his answers of the same day, D7 and D8). No ADR 0166 D10 trigger fires and none is added,
so there is no ADR 0166 amendment and no sign-off block here; the legal reading has its home in D7. The mandatory agents review the delivered diff in the PR against the conditions D7 lists.
**Deciders:** Klas Olsson (controller and product owner; directives and answers in chat on 2026-10-24, recorded in Context, D7 and D8) · `senior-cto-advisor`, "the CTO" below (decision-maker under
CLAUDE.md §9.2; the plan read, and the re-ruling inside Klas's override with its addendum; report-only, `docs/reviews/2026-10-24-share-plan-senior-cto-advisor.md` and `…-senior-cto-advisor-2.md`) ·
`security-auditor` (two plan reads, report-only, `docs/reviews/2026-10-24-share-plan-security-auditor.md` and `…-security-auditor-2.md`, the second with its *Tillägg*) · `design-reviewer` (two plan
reads, report-only, `docs/reviews/2026-10-24-share-plan-design-reviewer.md` and `…-design-reviewer-2.md`)
**Amends:** ADR 0165 (D4), by a dated amendment at the end of that file. Nothing above that amendment is rewritten. The notice link's label is ADR 0144's amendment of the same date (D3), not
this ADR's.
**Related:** [#2214](https://github.com/klasolsson81/jobbliggaren/issues/2214) (share an ad end to end; this is PR 1 of four; part of epic #2090, not `mvp`) · ADR 0165 (the public job page; D4, D5, D6) ·
ADR 0166 (D5, D7, D10 trigger 5 and the S4 (viii) reading) · ADR 0144 (row 10, Amendment 2026-10-24) · ADR 0160 (the anonymous DTO) · ADR 0053 (the modal; Amendment 2026-10-19, #2191, where
"Ansök" is reserved for the employer's application page) · ADR 0047 (flow comprehension) · ADR 0045 (budgets; Beslut 6) · ADR 0176 (D10, no prefetch) · ADR 0182 (the related section, whose place
after the article is unchanged) · ADR 0154 (every merge is a deploy) · ADR 0142 (the only brand marks, on `/logga-in`) · #2191 · #2092 · #2094
**Measured against:** the branch `feat/seo-share-landing` in `C:/tmp/jbl-seo-share`, cut from `5ad51c9c1` (main), at its four commits `c2dcb7739`, `65db9860f`, `a2a267ee5` and `dfbb23b9a`, read on
2026-10-24. The plan reads were taken at `5ad51c9c1` on the same day, with the PR's code uncommitted. Nothing here is merged, and the merge is the go-live.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. D1–D6 describe the delivered code, as the CTO's rulings
> and the two reviewers' plan reads shaped it. Points are cited by report: "CTO-1" and "CTO-2" are the CTO's first report and its second, inside Klas's override ("CTO-2 addendum" is the addendum to the second); "SA-1" and "SA-2" are `security-auditor`'s first and second plan reads (SA-2 includes her *Tillägg*); "design-1" and "design-2" are `design-reviewer`'s. D7
> follows `security-auditor`'s second plan read and records Klas's answers; D8 records Klas's override of the CTO's first ruling. The six reports are local files, transcribed verbatim by the
> driving session from the agents' hand-backs. Facts about the code were read at the four commits on 2026-10-24. The ADR lies in the ignored 0071–0199 range and needs `git add -f` and a README
> row (precedent 0167–0182).
>
> **Numbering.** 0183 was measured free on 2026-10-24, before this file was written: in this worktree, in every other `C:/tmp/jbl-*` worktree, in the main copy, on `origin/main` (whose last
> file under `docs/decisions/` is 0182) and in the open PRs (two Dependabot bumps, no ADR).

---

## Context

#2214 makes a shared job ad work end to end: a shared link unfurls as that ad, the person who opens it lands on a page that looks right, says how to apply and invites them to sign in, and "Dela"
exists wherever the ad is public. This ADR is the page, PR 1. Eight facts shape it.

- **Klas's directives, 2026-10-24, quoted without edits.**
  - On the per-ad preview, which is PR 2: *"våran delning måste vara smart, dvs skicka med länken, bilden (OG) måste vara tydligt och bra osv, inte för jobbliggaren, utan för annonsen osv."*
  - On the visitor, which is this PR: *"tänk även på att en som klickar på en delad annons, inte är inloggad. Därför måste kanske annonsen se väldigt bra ut, men samtidigt uppmana till att logga in.
    Nu när jag kollar på "offentliga" jobb-sidor så ser det inte så bra ut.. Öpnna annonsen.... på 2 ställen... Är du kontaktperson?? trots att ingen kontakt står med.... Ingen ansök knapp.... osv"*
  - After PR 1 was rendered: *"jag menar såklart dela annonsen, som inloggad främst, det är nog väldigt lite som kollar på den offenliga korten"*. So the signed-in surface is the main one, and it is
    the next PR.
  - After he showed Platsbanken's "Dela annonsen" row (Mejl, Facebook, X, Linkedin, Skriv ut annonsen), on the form of the share (the override, D8): *"som platsbanken, men vi behöver inte ha exakt
    samma knappar, vi kan ha fler eller färre, eller de som passar bäst. Dvs om jag klickar på facebook - ska ett delning starta, antingen som inlägg eller t ex messenger, med länk till annonsen...
    Att "gömma" allt bakom en knapp, gör det mer osynligt och fler klick.... i onödan"*.
- **The page before this PR** (read 2026-10-24 at `5ad51c9c1`, and live): "Öppna annonsen hos Arbetsförmedlingen" appeared twice, a secondary button under the dates and the only primary in the
  footer; there was no "Ansök", because `PublicJobAdDto` carries no `applicationUrl` (ADR 0160 D4, ADR 0171); the notice link read "Är du kontaktperson i annonsen?…" whether or not a contact was
  visible; and ADR 0165 D4 lists "a call to action" under *Left out on purpose*.
- **The notice link's presence is not Klas's to drop.** ADR 0166 D5 and D7 require the recruiter notice on every public page, and DESIGN.md §8 records the ratified deviation (ADR 0165 D4). Its
  wording and place can change; its presence cannot.
- **The sequence is four PRs, one session each** (Klas: "Sidan, bild, modal (Rek.)", then "Nästa PR direkt efter (Rek.)" for the modal). **PR 1:** this, the page. **PR 3:** Dela in the modal and on
  the signed-in full page, next. **PR 2:** the per-ad preview, which may go before 1b ("Ja, PR 2 får gå före (Rek.)"). **PR 1b:** Ansök, which the CTO split out as its own change-reason and gate
  (it widens the anonymous read by one member). Klas granted the ADR 0166 trigger-1 re-take for the application link "with the gate" ("Ja, med grinden (Rek.)": a link in phone form, to a personal
  profile, social media, a messaging channel or through a shortener is nulled, which is 39 of 36 288 links on listed ads, counted on the box 2026-10-24 06:51Z, counts only), and no fifth DPIA §8-B
  addendum ("Ja, inget tillägg (Rek.)"). Until 1b, the one link out is the page's one primary.
- **A shared link arrives cross-site and cookieless**, so the visitor passes the #2094 bounce and lands on `/jobb/<id>?vidare=1` (ADR 0165 D5). The address that is shared must therefore never be
  `location.href`.
- **The cookie policy promises no third-party script, analytics or tracking parameter.** A share row cannot break that.
- **ADR 0166 D10 trigger 5** is "Any **push** of a posting URL or its content to a third party" (the amendment of 2026-10-17). Every re-take so far has read it as not fired because nothing is pushed to anyone
  (CTO-1; S4 (viii); the Search Console amendment, point 2). A row that names Facebook, LinkedIn and WhatsApp makes the question live.
- **The house reserves "Ansök" for the employer's application page** (`applicationUrl`; #2191, ADR 0053 Amendment 2026-10-19).

## Decision

**The public job page `/jobb/<uuid>` is the page a shared link lands on for a visitor who is not signed in. It links out once, as its one primary action, in the head under "Sök jobbet". It invites
the visitor to sign in, in the card footer, with a plain `rel="nofollow"` link that brings the visitor back to the same ad. And it carries a visible share row, "Dela annonsen", after the notice
link: Facebook, LinkedIn, WhatsApp and E-post as plain anchors that carry the canonical address and nothing else, then "Kopiera länken" and "Fler sätt att dela" (the browser's share sheet, only where
it exists). Nothing is fetched from a platform before the click, and nothing is recorded at it. The row is one component that takes props only, so the signed-in modal can reuse it in PR 3. The legal
reading has one home, D7: no ADR 0166 D10 trigger fires.**

**Scope.** This ADR is the PR 1 slice of #2214. Ansök (PR 1b), the per-ad preview (PR 2) and Dela in the modal (PR 3) each amend this ADR where they change it, unless their own plan read rules
otherwise (the CTO). A new target, an icon, an identifier, a script or a request before the click is a new decision and not an edit of this one (D4, D7).

### D1 — The page for a signed-out visitor: one link out, in the head, under "Sök jobbet" (CTO-1 (a); design-1 1)

- **One link out.** In the head, after the facts row: a `section` with an `h2.jp-eyebrow` "Sök jobbet" (`publicjob.apply.heading`) and directly under it the page's one `--primary`,
  `<a target="_blank" rel="noopener noreferrer" class="jp-btn jp-btn--primary">` with the ad's `url` as `href`. Its label is the one ADR 0165 D4 sets and is unchanged: "Öppna annonsen hos
  Arbetsförmedlingen" for an `arbetsformedlingen.se` host or subdomain, "Öppna annonsen" otherwise. A value that is not `http` or `https` renders neither the link nor the heading. The footer's
  duplicate is removed.
- **Why in the head.** A visitor from a search result or a shared link should reach it without scrolling past the description; the duplicate in the footer existed for that reason, and a single
  link has to sit where that reason holds. The place in the head is `design-reviewer`'s call (CTO-1 (a) left it to her).
- **Why the heading.** The page is the ad, so "Öppna annonsen" does not say why to open it again. "Sök jobbet" is two words that state the purpose while the button states the target. It is also
  Platsbanken's own heading (design). Klas read the page as "Ingen ansök knapp", a guess in ADR 0047 point 1's sense (design).
- **Never relabelled "Ansök".** Its target is the ad page at Arbetsförmedlingen, and the house uses "Ansök" only for `applicationUrl`. The same name for a different function breaks WCAG 3.2.4
  (CTO-1 (a)). `design-reviewer` first proposed "Ansök via Arbetsförmedlingen" and withdrew it on this ground (design-1).
- **One `--primary`** (ADR 0038). The page has no other, and the test counts it.
- **PR 1b changes this head.** `design-reviewer` ruled the 1b state in advance: the head becomes h1, company, dates, facts, a text link "Visa annonsen på Platsbanken" on its own line (the
  signed-in page's `JobAdSourceLink`), and last "Ansök" as the `--primary`, mirroring #2191, with "Sök jobbet" deleted because "Ansök" says it. PR 1 does not ship that arm as dead code (YAGNI; CTO-1 (a)),
  and the web schema could not carry it. PR 1b amends this decision.

### D2 — The sign-in invitation: in the card footer, a plain `nofollow` link that returns the visitor to the ad (CTO-1 (f); design-1 2; SA-1 C-1)

- **Where and what.** The card footer, `jp-modal__foot jp-modal__foot--split` with `data-nosnippet`: an `h2` in the `h4` role "Spara annonsen och håll ordning på din ansökan"; one line, "Med ett konto
  kan du också bevaka företaget och se hur annonsen matchar dina yrken och kompetenser."; and the link "Logga in eller skapa konto", a `jp-btn--secondary`. The link text is the site footer's own
  `landing.footer.start.auth`, so it is not a new key. Each clause is something the signed-in page on the same URL offers (Spara, Bevaka, "Har du ansökt?", the
  match). The footer is where the duplicate stood, where the signed-in page has those actions, and it does not compete with the primary in the head. ADR 0182 D5's order (article, related, explainer
  links) is untouched. `data-nosnippet` keeps the invitation out of a search result's excerpt.
- **Never `--primary` or `--emphasis`.** On a short ad at 1920 px two green calls to action would be visible at once (design).
- **The target.** `/logga-in?next=` + `encodeURIComponent(publicJobAdPath(jobAd.id))`, which is `/logga-in?next=%2Fjobb%2F<id>`. It is built from the DTO's id through `publicJobAdPath`, never from
  the request URL or `searchParams` (SA-1 C-1). `safeRedirectPath` passes `/jobb/<id>` and swallows only `/` and `/jobb`; the test decodes the link's `next` and asserts the real
  `safeRedirectPath` returns it unchanged.
- **`rel="nofollow"`, required** (CTO-1 (f)). About 43 600 listed pages (security-auditor counted 43 635 on the box, 2026-10-24, counts only) would otherwise each link a distinct
  `/logga-in?next=…` URL, and `/logga-in` is `noindex` and crawlable (#2092). Google says not to use `noindex` for crawl budget, because the page is still requested and then dropped (*Crawl budget
  management*, updated 2026-07-22), and that links marked `nofollow` are "generally not followed" (*Qualify outbound links*, updated 2025-12-10). A `Disallow: /logga-in?` in `robots.txt` was
  rejected: it is a D6 amendment for what one attribute does, and it hides the `noindex`.
- **A plain `<a>`, not `next/link`: no prefetch.** The public group has none (ADR 0165 Amendment S5, ADR 0176 D10). `/logga-in` runs `getExternalLoginProviders()`, an API read (cached 300 s), and
  whether a prefetch would run it on an anonymous view is not measured; a plain anchor makes the question moot.
- **The way back, traced in code on 2026-10-24** (design-1's Major 1: "Spara annonsen" is true only if the visitor lands on the same ad, a new account included):
  - *One-time code.* The entry step writes `next` into the login-flow cookie (`cookieSafeNext(safeRedirectPath(…))`), verification keeps it, and `signedIn` redirects to `safeRedirectPath(flow.next)`.
  - *A new account.* `consentRequired` writes the consent phase with `next: flow.next`, and `completeRegistration` ends in `redirect(safeRedirectPath(flow.next))`. The consent step is on the way and
    does not lose the ad.
  - *OAuth.* The start route guards `next` again and the server holds it in its `OAuthFlow` record, keyed on `state`; the provider sees only the opaque `state`, never the ad id
    (`StartExternalLoginCommandHandler.cs:41-42`). The callback redirects to `safeRedirectPath` of the echoed `next` on `signedIn`, and keeps it into the consent phase on `consentRequired`.
  - *The link in the mail.* `next` is applied only when the link is opened in the browser that holds the flow cookie of that same challenge (`flow.phase === "code"` and the challenge id equal to
    the link's); otherwise the target is `/oversikt`. A link never leads to the consent step and carries no `next` of its own. For the e-mail path `next` sits in our own cookie, not in the mail.
  - *No onboarding redirect.* The `(app)` layout redirects a visitor with no session to `/logga-in` and nothing else, so no step between sign-in and `next` sends the visitor elsewhere.
  - Not walked end to end against the live providers.

### D3 — The notice link's label is reworded, signed and pinned (design-1 3; SA-1 (iv), M-1)

The label of the `/kontaktperson-i-annons` link on this page is now "Så behandlar vi uppgifter om kontaktpersoner i annonser" (en "How we process details about contact persons in job ads"),
`publicjob.recruiterNoticeLink`. It replaces a label that read as if a contact were shown, which Klas noticed ("Är du kontaktperson?? trots att ingen kontakt står med"). Its text, conditions and pins
are **ADR 0144 Amendment 2026-10-24** (row 10's third key), and this ADR does not restate them. Its place is unchanged, directly after the description inside `<article>`, so DESIGN.md §8's exception
and ADR 0165 D4's ratified deviation stand. The detail's and the panel's keys are unchanged.

### D4 — The share row: four plain links, text only, in the card body (Klas's override; CTO-2 (a), (b), (f), (g) and its addendum; design-2 2; SA-2 C-5–C-8)

- **Where.** `ShareJobAd` renders `<section aria-labelledby data-nosnippet>` with an `h2.jp-eyebrow` "Dela annonsen" (`h3` where the host asks for it), in the card body after the notice link and
  before the footer. Not in the head: a row of five to eight items there would push the description down (ADR 0165 D4) and compete with the primary. The end of the text is where the decision to share
  is made, and where Platsbanken has its row. `data-nosnippet`: a search result quotes the ad, not the page's controls. No lead sentence under the heading (DESIGN.md §8 rule 1).
- **One list, six items, in this order:** Facebook · LinkedIn · WhatsApp · E-post · Kopiera länken · Fler sätt att dela. The named platforms come first in alphabetical order, which ranks no
  company and survives a change of the selection; then the visitor's own channels, everything that works without script first and the two script-dependent items last. This is `design-reviewer`'s
  order; the CTO's first one had E-post first, and the CTO took hers (CTO-2 addendum (3)).
- **The four links** (`shareTargetLinks`), with `url` = `publicJobAdUrl(id)` and `title` = `publicJobAdTitle(jobAd)`:

  | Target | `href` | Parameters |
  |---|---|---|
  | Facebook | `https://www.facebook.com/sharer/sharer.php?u=<url>` | `u` only |
  | LinkedIn | `https://www.linkedin.com/sharing/share-offsite/?url=<url>` | `url` only |
  | WhatsApp | `https://wa.me/?text=<url>` | `text` = the url only |
  | E-post | `mailto:?subject=<title>&body=<url>` | `subject` and `body`, no recipient |

- **The canonical address and nothing else.** `url` is never `location.href` (D5's reason), never a `utm_*`, `quote`, `hashtag`, `via`, `app_id` or any other parameter. Facebook, LinkedIn and
  WhatsApp each build their preview from the page's own `og:title`, which is the same string as `publicJobAdTitle`. A pre-filled title would repeat the preview, and it would write a fixed copy of
  the employer's name into a post or a message, where no 410 and no erasure step reaches it (Art. 5(1)(c); ADR 0166 W4). For a sole trader the company name is a person's name, and a pre-filled
  text is the `text` member under another name (SA-2). The mail's subject carries the title: it is private correspondence that the visitor writes, under the household
  exemption (SA-2 (5)).
- **Encoding.** Every value goes through `encodeURIComponent`, never `URLSearchParams`. A `+` is a literal character inside a mailto value (RFC 6068 §2), so `URLSearchParams` would change a
  subject; a space is `%20`, and the test pins that no `+` appears. In the mail subject, runs of `\r`, `\n` and `\t` become one space, so an imported title cannot carry an encoded line break into a
  header; this is done at the mailto sink only, and the share sheet's title stays exactly `publicJobAdTitle`. `security-auditor` grades a breach of this a Blocker for `mailto` (SA-2 C-5) (a `&bcc=` injected through an
  employer-chosen title would send the visitor's mail, and its recipients, to an address the employer chose).
- **Anchors.** The three platforms: `target="_blank" rel="nofollow noopener noreferrer"`. `nofollow` keeps crawlers away from about 130 000 third-party share URLs (three per listed page).
  `noreferrer` drops the `Referer` (the global `strict-origin-when-cross-origin` policy already reduces it to the origin, so the attribute is the pinned second layer, and it becomes load-bearing in PR
  3, where the signed-in page's address can carry search state) and implies `noopener`. The mailto has no `target`. No `referrerpolicy` attribute beside `rel` (SA-2: one mechanism); the code sets none, and no test asserts its absence.
- **Accessible names say both the action and the target, and contain the visible text** (WCAG 2.4.4, 2.5.3): visually hidden text, never `aria-label`. A platform link reads "Dela via Facebook
  (öppnas i ny flik)" (`publicjob.share.via`, the name, `publicjob.share.newTab`), the mail link "Dela via E-post". GOV.UK's `share_links` component works the same way.
- **Text only, no icons.** `lucide-react` 1.51.0 has no brand icons (design measured `dist/esm/icons`: no facebook, linkedin or twitter). Generic icons on some items would make the row uneven and
  the platforms look subordinate. Brand marks exist only in the `/logga-in` exception (DESIGN.md §3, ADR 0142), whose owners' rules were read for sign-in and not for sharing; marks here need a new,
  bounded exception in DESIGN.md §3 and §7 with every owner's rules read, which Klas decides in a PR of its own. No icon library is added (BUILD.md §3.1; a new one is an AGENTS.md §12 class).
- **Form.** The house stand-alone text link, `text-body-sm font-semibold text-brand-700 underline underline-offset-2` (the underline is for WCAG 1.4.1), a target floor of 32 px that grows to
  44 px at ≤768 px (the pattern of `recruiter-contact-link.module.css`), `gap-x-5`, left-aligned.
- **No SDK, widget, iframe, pixel, `window.open` popup or request before the click** (CTO-2 (f); D7). An embedded plug-in that sends data when the page loads makes the site a joint controller
  (CJEU C-40/17, *Fashion ID*, 2019); a plain anchor sends nothing until the visitor clicks.
- **Why these targets** (CTO-2 (a); CTO-2 addendum (2)). A target needs a URL form with no registration and no script, and it must reach the audience. Internetstiftelsen, *Svenskarna och internet 2026*, share of
  internet users aged 8+ who used it in the past 12 months, read 2026-10-24: Facebook 69 %, Messenger 66 %, WhatsApp 51 %, LinkedIn 27 %, X 13 % (18 % in 2024 and 15 % in 2025), Threads 7 %,
  Bluesky 2 %.
  - *In:* Facebook (Klas named it; Meta's own sharing page says its dialog offers a post, a group or Messenger), WhatsApp (the second private-message route by reach, and growing), LinkedIn (the
    job field's own network, despite the lower reach) and E-post (universal, and it names no third party).
  - *X is out:* 13 % and falling, and not a job channel; Klas allowed "fler eller färre … de som passar bäst". `design-reviewer`'s conditions are necessary and X passes them; the reach criterion
    is what excludes it. If Klas wants it back, it is one row with `https://x.com/intent/tweet?url=<url>` (the url only).
  - *A separate Messenger link is out:* Meta's Send Dialog requires an `app_id` and "is not supported on mobile devices" (developers.facebook.com/docs/sharing/reference/send-dialog, read
    2026-10-24); `fb-messenger://share?link=` is undocumented and means nothing on desktop, so showing it only on mobile would need device sniffing. Mobile reaches Messenger through "Fler sätt att
    dela", and desktop through Facebook's dialog (Klas checks that live).
  - *Print ("Skriv ut annonsen") is out:* printing is not sharing, and it would need a print stylesheet of its own (`@media print` has 0 matches in `src/`, measured by design-2 and by CTO-2), which is a
    separate change-reason. The browser's own print exists.
  - *"Completes on every device"* (design's condition) is read as: the mechanism works on every device class without detection. Where completion goes through the visitor's own installed or configured
    app (a mail handler, a WhatsApp account), that is the visitor's environment and not a choice we make by device.
- **The endpoints.** `sharer.php` and `share-offsite` are not in their owners' documentation. Facebook's documented `/dialog/share` needs an `app_id`; LinkedIn's documented alternative is a script
  (`platform.linkedin.com/in.js`) that runs on page load. Platsbanken uses `sharer.php` (the CTO read it live). `wa.me/?text=` rests on vendor guides and Klas's live check: the vendor's own
  "click to chat" page could not be fetched on 2026-10-24. The dependency and its fallback are D8.

### D5 — The two buttons: copy and the share sheet, each reporting its own outcome (design-2 3–4; CTO-2 (c) and its addendum (1), (5); SA-1 C-2)

- **"Kopiera länken"** is a `<button type="button">` in an `li` with `noscript:hidden` (Tailwind 4.3.3's `noscript:` is `@media (scripting: none)`, measured in `node_modules`). It is rendered by the
  server with a fixed label, because the label no longer depends on the browser, and it does nothing until hydration, like the house's other server-rendered islands. On success the label becomes
  "Länken är kopierad" for three seconds and a new click restarts the time. Both labels sit in one grid cell, the inactive one `invisible`, so the button keeps the wider label's width and the row
  never re-wraps. An `sr-only` `role="status"` region, empty from the first paint, reads "Länken är kopierad".
- **"Fler sätt att dela"** is rendered by the server in an `li` with `noscript:hidden` and `invisible`. It becomes visible after mount, and only where `typeof navigator.share === "function"` and,
  where `navigator.canShare` exists, `canShare(data)` accepts the data (`hasNativeShare`; the server snapshot of `useSyncExternalStore` is `false`). Feature detection only, never the user agent: a
  device discriminator misclassifies touch laptops and iPadOS (MDN, *Browser detection using the user agent*). Its place is reserved from the first paint, so mounting changes no height; an invisible
  item is outside the tab order and hidden from assistive technology. The name is "Fler sätt att dela" and not "Fler sätt…", because a button's name is often read out of context (WCAG 2.4.6).
- **The payload is exactly `{ title, url }`**: `publicJobAdTitle(jobAd)`, the same string as `<title>` and `og:title`, and `publicJobAdUrl(id)`. No `text`, no `files`. The title is what the browser's
  own share menu sends, so the button adds nothing the browser would not. `text` would duplicate the receiving app's unfurl, some targets put it in front of the URL, and it would write a third,
  static copy of the company and the dates that no 410 and no erasure step reaches (Art. 5(1)(c); ADR 0166 W4). The url is never `location.href`: before `StripBounceMarker` runs, the address can carry
  `?vidare=1`.
- **Outcomes.** A shared sheet and a cancelled one (an `AbortError`) show nothing, and focus stays on the button; the visitor's cancel is never answered with a clipboard write they did not ask for.
  Any other refusal of the sheet, or a copy that fails or has no clipboard, shows the link in a read-only field under the list, in the same section and never inside the row: the label "Länk till
  annonsen", a `.jp-input` with `readOnly` and the url as its `value`, and a hint tied by `aria-describedby` ("Delningen kunde inte startas. Kopiera länken från fältet." or "Länken kunde inte kopieras.
  Kopiera den från fältet."). Focus moves to the field with the value selected. It is neutral in colour, with no `aria-invalid` and no `role="alert"`, at most 480 px wide, and stays until the page
  reloads. Because the url is in an input's `value`, the test that the page shows no internal id (it reads `textContent`) stays true.
- **No copy after a failed share.** The CTO's first ruling had "any other rejection → copy". It existed only because one control had to cover both jobs. Copy is now its own visible control; `share()`
  consumes the click's user activation (W3C Web Share, step 7), so a write chained after it can fail the way the field catches; and each control reports its own outcome (ADR 0047). `design-reviewer`
  ruled it and the CTO confirmed it (CTO-2 addendum (1)); SA-2 notes it has no security effect.
- **The re-entry guard.** While a sheet is pending a second click does nothing (a ref in the handler) and the button carries `aria-disabled="true"`; the label does not change, because the sheet is
  the state. So the page never causes an `InvalidStateError` itself.
- **An empty sheet** (the OS has no share target) ends in an `AbortError` (W3C Web Share 13.1), which looks like a cancel, so nothing happens. Accepted: the visitor saw no sheet, and the row's other
  items are beside it.
- **Layout shift at mount is 0**, measured (Verification, R2).

### D6 — The component shape that PR 3 reuses (CTO-2 (e); design-2 1)

- **Two builders, one knowledge each** (DRY). `publicJobAdPath(id)` in `lib/job-ads/public-job-ad-url.ts`, from which `publicJobAdUrl` is built (same output as before) and from which the
  invitation's `next` is built. `publicJobAdTitle(jobAd)` in `lib/job-ads/public-job-ad-title.ts`, used by `generateMetadata` and by the share title, so `<title>`, `og:title` and the shared title are
  one string.
- **Two pure modules for two change-reasons** (SRP, CCP). `lib/job-ads/share-job-ad-links.ts`: `shareTargetLinks(data)`, the fixed order and the platforms' URL forms. `lib/job-ads/share-job-ad.ts`:
  `hasNativeShare`, `shareNatively` (`shared` | `cancelled` | `failed`) and `copyLink` (`copied` | `failed`) against a navigator-shaped `SharePort`, tested without a browser.
  `aktivitetsrapport/copy-button.tsx` is not generalised: its silent-failure contract is different.
- **`ShareJobAd`** (`components/job-ads/share-job-ad.tsx`) has no `"use client"`, no hooks and no translations. It takes `{ data, labels, headingId, headingLevel?: 2 | 3 }` and renders the section,
  the heading and the island. The labels are one typed record, `ShareJobAdLabels`, resolved by the caller (here the Server Component, from `publicjob.share.*`), so it works in the public page and
  inside PR 3's client modal shell alike (DIP, ISP). The house precedent for labels as props is `jobb-results.tsx`, `feedback-form.tsx` and `foretag-sok-*`.
- **`ShareJobAdControls`** (`components/job-ads/share-job-ad-controls.tsx`, `"use client"`) is the island, and **it also renders the four links.** This is a deliberate deviation from the CTO's
  sketch (CTO-2 (e)), where `ShareJobAd` rendered the links and the island, named `share-job-ad-actions.tsx`, held only copy, Web Share, the status region and the field. The reason: `design-reviewer`
  ruled one `<ul>` with one `<li>` per item, all six in one list, and an island beside the list would have put the two buttons outside it. A client component is still rendered on the server, so the
  four links are in the first response and work without script (the no-JS renders confirm it). The cost is that `shareTargetLinks` and the links' markup also ship in the island's client bundle. That
  delta is not measured in this ADR.
- **What PR 3 carries** (CTO-2 addendum; SA-2 (4), M-2; design-2 1):
  - The same `ShareJobAd`, as the last child of `JobAdDetail`'s body, after the description and the contact card with its notice, with `headingLevel={3}` when `inModal`, and the same labels and order on
    both surfaces (WCAG 3.2.4). Not in the footer: it is full, and five controls broke it into two rows (#2193).
  - **The url is always `publicJobAdUrl(id)`**, never the modal's route or any URL with search state (the modal takes over the list's `searchParams`). No user id, session, referral code, `utm` or
    `via`, and nothing recorded at the click: no event, Server Action, `fetch`, `sendBeacon`, `after()`, log line or audit event. Only `title`, `companyName` and `id`, never `contacts`,
    `description` or `applicationUrl`. The guest modal is outside this reading.
  - **The row renders only for an ad that is publicly listed.** The signed-in read returns every ad that is not `Erased`, and `source` is user-settable (`POST /api/v1/job-ads` accepts
    "Platsbanken"), so a link to such an ad answers 404 or 410, and a mail subject would carry a title the public surface does not carry (SA-2 M-2; design-2 1: a link that leads to a 410 is a
    task the receiver cannot complete, ADR 0047 point 1). **Listedness comes from the backend, as a value computed by `JobAdPublication.IsListed(now)` and carried on the modal's read; it is never
    re-derived on the client from status, source and expiry** (CTO-2 addendum; SA-2 M-2). A second copy of the publication predicate drifts (DRY), and it is the class that
    SA-1's proposed trigger 19 names, "a second copy of the gate". Whether that needs a DTO member is PR 3's plan read, with `dotnet-architect`.

### D7 — The ADR 0166 D10 reading, and the binding on agents (SA-2 and her *Tillägg*; Klas's answers)

- **The single home** of the legal reading for the share row is this decision (SA-2 C-10). It adds no D10 trigger: trigger 5, read with S4's criterion, already covers every automatic
  transfer (SA-2). ADR 0166 is not amended.
- **Trigger 5 is not fired**, in PR 1 or PR 3, by plain anchors that carry no identifier from us. The trigger concerns a "push … to a third party", and all its examples are transmissions we make.
  The criterion signed in S4 (viii) is that "we send nothing to anyone" and that a pull is "not a transmission by us"; the Search Console amendment (point 2) applies the same criterion. An anchor sends no byte
  from our origin, neither when the page renders nor at the click. The only request is the one the visitor's browser makes when the visitor clicks.
  - *The content is nothing new.* The link carries the canonical address, and the mail also the title. Both are already on the page, and the platform's own link-preview fetcher reads them there;
    that recipient is already named in ADR 0166 D8 point 2 ("search engines, link-preview fetchers and archives are recipients, each an independent controller for its own processing").
  - *The CTO's objection that the row names the third party* (CTO-1 (b)). Every outgoing anchor does: "Öppna annonsen" points at Arbetsförmedlingen with its id, and `security-auditor` read Ansök in
    1b outside trigger 5 on the same ground (SA-2 (1)). What makes the reading hold is that the form is pinned (the conditions below), not that no service is named.
  - *Fashion ID* grounded joint responsibility in an embedded plug-in that sent data when the page loaded, without the visitor doing anything. A plain link sends only when the visitor clicks.
- **What stays inside trigger 5** (SA-2 (1); this ADR writes it in, as S4 did):
  - an SDK, a social plug-in, an iframe, a pixel, or an icon, a font or an image that is fetched from a platform;
  - `preconnect`, `dns-prefetch`, `prefetch`, `prerender` or Speculation Rules for a platform's origin;
  - a request when the page is rendered, or without the visitor having clicked;
  - an API for the number of shares or any other engagement statistic;
  - an app id, an account, a handle or `via` that is ours;
  - a post that we publish automatically.
- **Other triggers, not fired.** Trigger 1: no new public page and no new value; in PR 3 the modal is a signed-in surface. Trigger 15: no value from another ad. Triggers 6–7: no change to the
  JSON-LD (`job-posting-json-ld.ts` and its test are byte-unchanged). Triggers 8–12: the row is never on a row of the map or the catalogue. The sign-in invitation: a plain link that carries only the
  page's own address and reads nothing new, so the sign-in provider never learns which ad (D2).
- **The conditions that hold the reading** (SA-1 C-1–C-3 and SA-2 C-5–C-10; a breach is a finding at the PR review, graded Blocker for `mailto`):
  - the forms, parameters and encoding of D4, parsed in the test with `new URL()`, with a hostile title (`&bcc=x@example.com`, `&body=`, `#`, `%`, `+`, `?`, CR, LF, CRLF, TAB) that comes back as plain
    data with no added parameter;
  - nothing before the click: none of the items in the list above;
  - no identifier from us and nothing coming back: no app id, account, `via`, `hashtags`, `related`, `utm`, share id, user id or session; no `redirect_uri`, callback route or share-count API;
  - nothing recorded: no `fetch`, logging, counting, Server Action, `sendBeacon` or storage at the click;
  - the old test "every link out is the ad's `url`" is replaced by "the links out are exactly the ad's `url` plus one per target from the builder", and the row exists only on a listed 200 page,
    never on a row of the catalogue or in the related section;
  - the payload is exactly `{ title, url }`, no `files`;
  - this PR adds no DTO member, no read and no `deploy/` change, and `publicJobAdUrl` gives the same output as before (S4's property that the map's `loc` comes from it).
- **No CSP or Permissions-Policy change, and no policy text.** Neither `navigator.share` nor `clipboard.writeText` makes a network request, so `connect-src 'self'` is unaffected; the CSP mechanically
  blocks an icon, an SDK or an iframe fetched from a platform; `web-share` and `clipboard-write` default to `self`, which a top-level page with `frame-ancestors 'none'` satisfies. The row sets no cookie,
  stores nothing and loads nothing from a third party, so the cookie and privacy policies stay true as they stand. What the platform collects after the click, it collects as an independent controller,
  as after any outgoing link.
- **A registered Meta app against `sharer.php`** (SA-2 (2)). `sharer.php`: no contract and no identifier from us, and the request is the visitor's, so no Art. 28 relationship, no joint
  responsibility under Art. 26 and no Chapter V transfer by us; no new policy, register or cookie line. `dialog/share` with our `app_id`: (a) Klas would accept Meta's Platform Terms as a developer, and Meta then
  requires a privacy policy of our own that describes platform data and deletion; (b) every visitor's request to Meta would carry our app id, so Meta records that the share was made through our app,
  a datum about the visitor that comes of our choice and is not needed to share (Art. 5(1)(c), 25(2)); (c) whether a registered app gets share statistics is not measured, and if it did we would be
  joint controllers with Meta (CJEU C-210/16, *Wirtschaftsakademie*), which needs an Art. 26 arrangement, its core in the policy, an Art. 30 row and a Chapter V assessment; (d) a request made in our
  registered app's name is no longer only the visitor's own navigation, and she would not read it outside trigger 5 without a re-take (S4 (viii): "a push would need its own Chapter V reading"). Her
  Blocker B-1 (SA-2) applies only to that variant. It is not built here, and building it needs a plan read of its own.
- **The binding on agents** (CTO-2 (f), with SA-2's addition in the *Tillägg*; binding whatever the trigger-5 reading): **no agent session puts a posting URL into a third-party share endpoint.** An agent's
  navigation is not a visitor's act, and the Search Console amendment (point 3) already counts an agent session as another caller inside trigger 5. Live checks use `https://jobbliggaren.se/` as the
  payload, and **stop at the platform's sign-in or compose page: they never sign in at the platform and never publish anything.** Klas checks with a real ad, from his own device. The render tool for
  this PR never clicks a platform link.
- **The erasure runbook** (SA-2 Minor 1, Art. 17(2)). The platforms become known holders of previews. `docs/runbooks/recruiter-pii-erasure.md` §2.4a gains a step that asks Facebook's Sharing
  Debugger (*Scrape Again*) and LinkedIn's Post Inspector to re-read the address after the 410 has been verified, with Klas's own accounts and never an agent. Whether either tool drops its copy on a
  410 is not measured. It rides in this PR.
- **Klas's answers, 2026-10-24,** by AskUserQuestion, to `security-auditor`'s escalations and the CTO's information. They are quoted without edits:

  | # | Asked by | His answer | What it decides | Recorded in |
  |---|---|---|---|---|
  | 1 | `security-auditor`, SA-1 question 1 | "Ja, ingen trigger (Rek.)" | PR 1 fires no D10 trigger: the sign-in invitation, the share button and the copy, and the notice link's new label, which she signed | here (D2, D7), ADR 0144 Amendment 2026-10-24 |
  | 2 | `security-auditor`, the question she asked again in SA-2 for the named services | "Ja, ingen trigger (Rek.)" | the share row of named services fires no D10 trigger, on the reading and the pinned form above | here (D7) |
  | 3 | the CTO, CTO-2's information on the targets | "CTO:ns fyra (Rek.)" | the row is Facebook · LinkedIn · WhatsApp · E-post · Kopiera länken · Fler sätt att dela, as text with no icons; X, Messenger and Print are out; no Meta app | here (D4) |
  | 4 | `security-auditor`, SA-2 Minor 1 | "Ja, lägg till steget" | the erasure runbook step for the two re-scrape tools, with Klas's own accounts | the runbook (this PR) |

### D8 — The override record: the CTO's single Web Share control, Klas's override, and what became of each of its three reasons (CTO-1, CTO-2 and its addendum; Klas's answers)

- **The CTO's original (b) and (c)** (CTO-1, `docs/reviews/2026-10-24-share-plan-senior-cto-advisor.md`): "The Web Share API with a copy fallback. No plain share links, in PR 1 or PR 3", and one control
  named by what it does, rendered after mount, on desktop as well as phones. Three reasons:
  1. *Trigger 5 stays trivially unfired.* With Web Share and copy the visitor picks the recipient in the OS sheet or pastes the link, and we name no one. With
     `facebook.com/sharer.php?u=<posting URL>` the third party and the URL in its query are both ours, which makes the reading arguable, and an arguable reading of a signed property costs a Klas
     re-take for no product gain.
  2. *Undocumented dependencies.* `sharer.php` and LinkedIn's `share-offsite` are undocumented; Facebook's documented dialog needs an `app_id`, which is a platform relationship; X's intent host has
     moved once already; and a fitness function cannot pin an undocumented third-party URL (Ford/Parsons/Kua 2017).
  3. *Civic tone.* Three brand marks pick favourites. The OS sheet already lists the visitor's own apps, and Platsbanken is a reference for the flow, not an obligation.
- **Klas's override**, after PR 1 was rendered and he had shown Platsbanken's row, verbatim: *"som platsbanken, men vi behöver inte ha exakt samma knappar, vi kan ha fler eller färre, eller de som
  passar bäst. Dvs om jag klickar på facebook - ska ett delning starta, antingen som inlägg eller t ex messenger, med länk till annonsen... Att "gömma" allt bakom en knapp, gör det mer osynligt och
  fler klick.... i onödan"*. It is deliberate: Klas has the last word, and the CTO then chose the targets inside it and did not reopen it.
- **What the override removes from the CTO's ruling** (CTO-2 (c)): "one control named by mode"; the CTO's rejection of an always-visible "Kopiera länken" (copy is now the desktop route to everything that is not
  listed); and the automatic copy after a failed share (D5). **What survives:** feature detection only, rendering after mount, a name that says what the control does, `AbortError` means nothing, the
  re-entry guard, and the canonical url.
- **What became of each reason:**
  1. *Trigger 5* is answered by `security-auditor`'s reading and the pinned form (D7), and by Klas's "Ja, ingen trigger (Rek.)" twice.
  2. *The undocumented endpoints* are an accepted trade-off with a named fallback. If Meta retires `sharer.php` the visitor sees an error page on Facebook and still has five other routes. The fix
     is either to delete the row, which closes mechanically because the targets are a data list and E-post, copy and the share sheet remain, or the documented dialog, which needs Klas to register a
     Meta app and a separate review by `security-auditor` (D7, SA-2 B-1). Nothing tests the endpoints' availability, only our own strings, and a breakage is noticed only by the people who use them.
     Klas confirms from his own device after the merge that a share starts on Facebook (as a post or in Messenger), on LinkedIn and on WhatsApp; that is DoD point 4 for those targets.
  3. *Neutrality* is answered by selection by measured reach (D4), alphabetical order of the named platforms, no brand marks, and no script.

## What is not measured, and the measurements owed

**Not measured, and not claimed:**
- That `sharer.php`, `share-offsite` and `wa.me` start a share today. No agent clicks them (D7); Klas does, from his own device, after the merge.
- Whether Facebook's Sharing Debugger or LinkedIn's Post Inspector drops its copy of an address on a 410.
- Whether a prefetch of `/logga-in` would run `getExternalLoginProviders()` on an anonymous view (D2).
- The size of the island's client bundle with the four links in it (D6).
- How often the row is used. Nothing is recorded at the click, by design (D7), so there is no usage signal.
- The sign-in paths against the live providers; they were traced in code (D2).

## Alternatives considered

### Alt A — The chosen design (D1–D8)
**For:** one link out and a reason to take it; an invitation that returns the visitor to the same ad; a row that works without script and in which each control does one thing; nothing leaves our origin
until the visitor clicks, and nothing is recorded at it; one set of builders, so the title and the address the page asserts and the ones it hands on cannot differ; one component PR 3 can reuse.
**Against:** the trade-offs under Consequences.

### The forks
- **Web Share plus copy, one control** (the CTO's first ruling). Overridden by Klas (D8).
- **X in the row.** Rejected: 13 % and falling, not a job channel. One row to add (D4).
- **A separate Messenger link.** Rejected: it needs an `app_id` and is not supported on mobile; `fb-messenger://` is undocumented (D4).
- **"Skriv ut annonsen".** Rejected in PR 1: printing is not sharing, and it needs a print stylesheet the app does not have (D4).
- **Facebook's `/dialog/share` with an `app_id`.** Rejected: a contract with Meta, our app id on every visitor's request, and possibly joint responsibility (D7).
- **LinkedIn's JS plugin, SDK buttons and share aggregators.** Rejected: a third-party script that runs on page load (Fashion ID; SA-1 C-2, SA-2 C-6).
- **`sms:` links.** Rejected: iOS and Android parse the body parameter differently, and the share sheet covers SMS.
- **UTM parameters.** Rejected: tracking, and they make URLs that are not the canonical.
- **A pre-filled title or text in Facebook, LinkedIn or WhatsApp.** Rejected: the preview already carries it, and the copy outlives the ad (D4).
- **An automatic copy after a failed share.** Rejected: one control, one action, one outcome (D5).
- **Copy-first on desktop, or any device discriminator.** Rejected: it misclassifies touch laptops and iPadOS (D5).
- **A reserved empty slot after the links for the script-dependent items** (the CTO's first shape, and design's first). Replaced by items in the same list, `invisible` and `noscript:hidden` (D5).
- **The share row in the head.** Rejected: it pushes the description down and competes with the primary (D4). **In the footer:** the footer holds the invitation, and in the modal it is full (D6).
- **Icons, generic or brand, on the row.** Rejected (D4).
- **"Ansök" in PR 1, or relabelling the link out "Ansök" or "Ansök via Arbetsförmedlingen".** Rejected: two change-reasons and one legal gate in a single PR (SRP, CCP), and WCAG 3.2.4 (D1).
  PR 1b widens the anonymous read by one member, which ADR 0160 D4 pins by a contract test and a wire test, and which needs Klas's re-take and `security-auditor`'s signature on the final head (CTO-1 (a)). Bundled,
  every design iteration of the page would move that head and reopen a legal sign-off.
- **A `publicjob` subtree in the message provider** (labels as props instead). Rejected: it would put the share keys into every public-job document, against ADR 0045 Beslut 6's ratchet, and then be
  needed in `(app)` too (D6).
- **The invitation without `next`.** Rejected: it loses the ad. **With `Disallow: /logga-in?`:** rejected (D2). **As `--primary` or `--emphasis`:** rejected (D2).
- **`{ title, text, url }` as the share payload.** Rejected (D5).
- **Generalising `aktivitetsrapport/copy-button.tsx`.** Rejected: its silent-failure contract is different (D6).

## Consequences

### Positive
- A visitor from a shared link meets one link out with a reason beside it, the share row where the decision to pass the ad on is made, and an invitation that returns them to the same ad, new
  account included.
- The row works without script (four links), each control reports its own outcome, and mounting moves nothing.
- No third-party script, resource or identifier, and nothing stored, logged or counted. The cookie and privacy policies need no new sentence.
- `<title>`, `og:title` and the shared title are one string, and the canonical, the sitemap's `loc` and the shared address are one builder.
- `ShareJobAd` takes props only, so PR 3 reuses it on the signed-in surface with its own labels.

### Negative and accepted trade-offs
- **Two undocumented endpoints** (`sharer.php`, `share-offsite`), and `wa.me/?text=` resting on vendor guides. Nothing tests them; a breakage is noticed by people using them (D8).
- **WhatsApp on desktop is clumsy** without the WhatsApp app: a landing page or WhatsApp Web (CTO-2).
- **A share sheet with no target ends in an `AbortError`** and does nothing (W3C Web Share 13.1). The visible routes beside it soften this.
- **Until PR 2, a shared link's preview carries the page's title and description but the shared Jobbliggaren image** (ADR 0165 D4: "There is no per-ad image"). That is what Klas's first directive
  wants changed, and PR 2 amends it.
- **The platforms become known holders of previews**, which the runbook step asks them to re-read after a 410, with an outcome that is not measured (D7).
- **The island is larger than CTO-2's sketch,** by the four links' markup and `shareTargetLinks` (D6).
- **No usage signal,** because nothing is recorded at the click (D7).
- **Three share URLs per listed page,** about 130 000 in all, kept from crawlers by `nofollow`.
- **The head changes again in PR 1b** (D1), and the modal's listedness is PR 3's plan read (D6).

## Implementation

One PR on `feat/seo-share-landing`, frontend only, PR 1 of four on #2214. It adds no migration, no package, no backend file, no DTO member, no read and no diff under `deploy/`, and no icon library.
`messages/{sv,en}/publicjob.json` is the only i18n file, under Klas's share of `hotspot:i18n` with #1977 ("Dela, bara publicjob.json (Rek.)"). The commits:

1. **`c2dcb7739`, `refactor(web)`:** `publicJobAdPath` and `publicJobAdTitle`; `publicJobAdUrl` is built from the former; `generateMetadata` uses the latter.
2. **`65db9860f`, `feat(web)`:** `share-job-ad.ts`, `share-job-ad-links.ts`, `ShareJobAd`, `ShareJobAdControls` and their tests.
3. **`a2a267ee5`, `feat(web)`:** `PublicJobAd`: the link out once under "Sök jobbet", the footer's invitation, the share row placed after the notice; the `publicjob` keys in sv and en.
4. **`dfbb23b9a`, `docs(adr)`:** ADR 0144 Amendment 2026-10-24, and `publicjob.recruiterNoticeLink` pinned as row 10's third key in `legally-bound-copy.test.ts`.
5. **Then the docs:** this ADR and the amendment to ADR 0165; DESIGN.md §6, one point on the public job page (the head, the footer's invitation, the share row); `docs/runbooks/recruiter-pii-erasure.md`
   §2.4a, the re-scrape step; the README row; the session docs (ADR 0065).

**What comes back** (nothing detects these automatically; each is read when the event is proposed):
- A new target or a changed URL form: one row in `shareTargetLinks`, as plain as the others. Anything with an app id, a script or an identifier is a trigger-5 question first (D7).
- Icons or brand marks: a bounded DESIGN.md §3 and §7 exception, in a PR of Klas's own (D4).
- Facebook's documented dialog: a plan read of its own (D7).
- PR 1b, PR 2 and PR 3 amend this ADR where they change it (Scope).

**Test premises (AGENTS.md §5, Tests).** The off-site `href`s are read from the rendered page and compared with the builder's output. The hostile title is built from the characters an imported title
can contain. The sheet and the clipboard are stubbed with what the browser's own API returns and rejects (`AbortError`, `NotAllowedError`, `TypeError`, `DataError`, `InvalidStateError`). No fact about
production is asserted off a hand-seeded row.

## Verification

Each reading below is dated and has its source. Nothing is estimated.

| Reading | What | Recorded |
|---|---|---|
| R1 | the tests | 2026-10-24, at `dfbb23b9a`, run by `adr-keeper` for this ADR: seven files, 142 tests, all passing — `share-job-ad-links.test.ts`, `share-job-ad.test.ts`, `public-job-ad-url.test.ts`, `share-job-ad.test.tsx`, `public-job-ad.test.tsx`, `legally-bound-copy.test.ts` and `(public-job)/public-job-pages.test.tsx`. One assertion in the last was narrowed from "no `[data-nosnippet]`" to "no related section", because the footer and the share row now carry `data-nosnippet` too. |
| R2 | the render matrix | 2026-10-24, Playwright against `next dev` and a stub of the API: 26 renders, in the light theme, the only one the app renders (`DARK_MODE_ENABLED = false` in `theme-provider.tsx`). Rest, rest with a share sheet, copied, failed copy, failed share and no script at 375, 1280, 1920 and 3440 px, plus English and keyboard at 1280 px. Axe (`wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`) reports 0 violations, no page error occurred, and no `layout-shift` entry of any kind was recorded at mount, in the 22 renders that ran script; the four no-script renders run neither measure. At 375 px every item is 44 px high, and "Kopiera länken" is 114 px wide both at rest and when it reads "Länken är kopierad". Without script the four links render and the two buttons' items are `display: none`. The render tool never clicks a platform link. The evidence is local: `C:/tmp/jobbliggaren-visual/2214-pr1/` (the screenshots and `results.json`). |
| R3 | the listed pages | `security-auditor`'s count on the box, 2026-10-24 (SA-1), counts only (CLAUDE.md §9.2 (a)): 43 635 listed ads. |

The tests pin, in the order of the decisions:
- **D1:** exactly one `--primary`; the link is in the head under the `h2` "Sök jobbet", with `target="_blank"` and `rel="noopener noreferrer"`; the footer holds no link to the ad's address; the plain
  label for a host other than Arbetsförmedlingen; no link and no heading for an empty address.
- **D2:** the footer has `data-nosnippet` and an `h2`; the link is `rel="nofollow"`, `jp-btn--secondary`, without `target`, with the exact `href`, and its decoded `next` passes the real
  `safeRedirectPath` unchanged; the invitation survives an empty address.
- **D4:** the links out are exactly the ad's address plus the four targets from the builder, with no `utm_`, no `script[src]`, no `iframe` and no `img` from another host; every `https` link that is not
  Arbetsförmedlingen's has `rel` exactly `nofollow noopener noreferrer`; the order; host and path; exactly one parameter per platform, decoded to the canonical; no employer name in a platform link; the
  mail's keys exactly `subject` and `body`; the hostile title; `%20` and no `+`; the accessible names with no `aria-label`; the mailto without `target`.
- **D5:** the two buttons' `noscript:hidden` and the share item's `invisible`; the sheet shown only where it exists and handed exactly `{ title, url }`, with nothing written to the clipboard; a cancel
  shows nothing; one sheet at a time with `aria-disabled`; every other refusal shows the field and copies nothing; the three-second confirmation that keeps the button's width, and its status region; a
  failed copy shows a labelled, read-only, focused and selected field with no `aria-invalid` and no alert, outside the list.
- **D6:** `publicJobAdPath`, `publicJobAdUrl` and `publicJobAdTitle`.
- **D3:** the notice link's text, place and target, and no contact block (`public-job-ad.test.tsx`); the key whole in sv and en as row 10 (`legally-bound-copy.test.ts`).

## References

- Code, at the four commits: `web/jobbliggaren-web/src/components/job-ads/public-job-ad.tsx` · `share-job-ad.tsx` · `share-job-ad-controls.tsx` ·
  `web/jobbliggaren-web/src/lib/job-ads/share-job-ad.ts` · `share-job-ad-links.ts` · `public-job-ad-url.ts` · `public-job-ad-title.ts` ·
  `web/jobbliggaren-web/src/app/(public-job)/publik/jobb/[id]/page.tsx` · `web/jobbliggaren-web/messages/{sv,en}/publicjob.json`
- Tests: `share-job-ad-links.test.ts` · `share-job-ad.test.ts` · `public-job-ad-url.test.ts` · `share-job-ad.test.tsx` · `public-job-ad.test.tsx` ·
  `web/jobbliggaren-web/src/lib/i18n/legally-bound-copy.test.ts` · `web/jobbliggaren-web/src/app/(public-job)/public-job-pages.test.tsx`
- The sign-in paths: `web/jobbliggaren-web/src/lib/auth/safe-redirect.ts` · `lib/auth/challenge-actions.ts` · `app/api/auth/oauth/[provider]/start/route.ts` and `…/callback/route.ts` ·
  `src/Jobbliggaren.Application/Auth/Commands/StartExternalLogin/StartExternalLoginCommandHandler.cs`
- `docs/reviews/2026-10-24-share-plan-senior-cto-advisor.md` · `…-senior-cto-advisor-2.md` (with its *Addendum*) · `…-design-reviewer.md` · `…-design-reviewer-2.md` · `…-security-auditor.md` ·
  `…-security-auditor-2.md` (with its *Tillägg*), all transcribed verbatim by the driving session; local · `docs/runbooks/recruiter-pii-erasure.md` §2.4a
- ADR 0038 · ADR 0045 (Beslut 6) · ADR 0047 · ADR 0053 (Amendment 2026-10-19) · ADR 0142 · ADR 0144 (Amendment 2026-10-24) · ADR 0154 · ADR 0160 (D4) · ADR 0165 (D4, D5, D6, Amendment
  2026-10-24) · ADR 0166 (D5, D7, D8, D10 triggers 1, 5–12, 15 and the amendments of 2026-10-17, 2026-10-18 for S4 and 2026-10-18 for the Search Console path) · ADR 0171 · ADR 0176 (D10) · ADR 0182
- AGENTS.md §5, §8, §12 · CLAUDE.md §9.2 (the reading rule (a)), §9.6 · DESIGN.md §3, §6, §7, §8 · BUILD.md §3.1
- Martin, *Clean Architecture* (2017), ch. 7, 10, 11, 13 · Hunt/Thomas, *The Pragmatic Programmer* (1999), DRY · Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017)
- W3C, Web Share API, editor's draft read 2026-10-24: `share()` steps 5–7 and 13.1 · RFC 6068 §2 · WCAG 2.1: 1.4.1, 2.4.4, 2.4.6, 2.5.3, 3.2.4, 4.1.2 · MDN, *Browser detection using the user agent* ·
  GOV.UK Service Manual, *Building a resilient frontend using progressive enhancement*, and the `share_links` component · CJEU C-40/17 *Fashion ID* (2019), C-210/16 *Wirtschaftsakademie* (2018)
- Internetstiftelsen, *Svenskarna och internet 2026*, "Sociala medier", diagram 3.2, read 2026-10-24 (https://svenskarnaochinternet.se/rapporter/svenskarna-och-internet-2026/sociala-medier/); the 2025
  edition for the earlier X figures · Meta for Developers, Send Dialog reference and Share Dialog reference, read 2026-10-24 · Google Search Central, read 2026-10-24: *Crawl budget management*
  (updated 2026-07-22), *Qualify outbound links* (updated 2025-12-10). These pages change; the dates say what was read.
- #2214 · #2090 · #2191 · #2092 · #2094 · #2193
