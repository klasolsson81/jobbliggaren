# senior-cto-advisor — PR #1510 (#1505)

- **Date:** 2026-08-24
- **PR:** [#1510](https://github.com/klasolsson81/jobbliggaren/pull/1510) · **Head:** `7adb1a0f` · **Base:** `f3846dbf`
- **Three consultations in one thread:** (1) mechanism home, (2) an `aria-busy` bind re-bound on measurement, (3) routing two `design-reviewer` Majors that collided with the bind.

---

## 1. Mechanism home — **variant A bound**

The region-before-message mechanism moves to `src/components/common/announcer.tsx`; `/foretag/sok` and `/jobb` consume the same module. Exports `Announcer` (provider) + `Announce`.

**Grounds:** DRY (Hunt/Thomas 1999 kap. 7) — region-before-message is ONE piece of knowledge. REP/CCP/CRP (Martin 2017 kap. 13) — the module changes when the WCAG rule or React's commit ordering changes, not when a surface changes, so it belongs in neither surface's folder. SRP (kap. 7). §6 "one change-reason per PR" — change-reason is the *reason*, not the *surface*; the `/foretag/sok` half is import paths and an identifier.

**Change-reason (verbatim):**

> The search-result surfaces announce their load cycle through a live region that exists before the message, from one shared mechanism (WCAG 4.1.3 / ARIA22).

**Rejected:** B (`JobbAnnouncer` in `job-ads/`) — "exactly the quick fix the issue warns about, and worse than the status quo: today there is one pattern and one defect; after B there are two competing patterns that both look right." C (move but keep a copy) — "gives both duplication AND a shared module."

**Bound constraint (later superseded in part, see §3):** the `/foretag/sok` diff shall be a pure name/path change — zero behaviour change, zero changed announced sentence, zero changed test assertion. *"If the build notices that it changes a `/foretag/sok` assertion, the move has stopped being mechanical: STOPP to Klas rather than continue."*

**Branch list bound:** count, empty, rateLimited, error. `unauthorized` excluded — it `redirect()`s before rendering, so it presents no status message. The browse-all ground from `/foretag/sok` was tested against `/jobb` and does not exist here: the toolbar's ternary is total.

---

## 2. `aria-busy` — **bind withdrawn on the driving session's measurement**

The original bind said `JobAdListSkeleton` should lose `role`, `aria-live` **and** `aria-busy`, on the ground that *"it has no meaning outside a live region."*

**Verdict: "My sentence … was WRONG and is struck from the bind."**

- **Measurement 1 (repo):** `foretag-sok-results-skeleton.tsx:24` is `<div aria-busy="true" className="mt-8">` — no role, no `aria-live`, `aria-busy` kept, with the reason written on line 15 and pinned in `foretag-sok-results-skeleton.test.tsx:31`. *"That I had the file in context and still claimed the opposite is my error, not a matter of interpretation."*
- **Measurement 2 (W3C):** `aria-busy` is a global state on `roletype`, valid on base-markup elements. §6.6.2's grouping under Live Region Attributes is a table of contents, not an applicability rule — *"exactly the derivation my sentence was built on."*
- **Consequence the bind missed:** striking it on `/jobb` is a behaviour change no finding asked for, and it forces either divergence between the surfaces (the whole point of variant A) or a strike in `/foretag/sok` too — which the same bind forbids. *"The bind contradicted itself two bullets later."*

---

## 3. Routing two `design-reviewer` Majors — **both in-block, same PR**

### Collision 1 — her Major 3 (`role="alert"` + `Announce` on the rate-limit branch) vs the bound constraint → **(a): strike on BOTH surfaces, in this PR**

> **My bound constraint does not reach this case, and I say it plainly: its letter does, its purpose does not.** The constraint was written against **unreviewed drift** — a "mechanical" move silently becoming a behaviour change nobody reviewed. Here the behaviour change is neither silent nor unreviewed: it is ordered, in writing, with file and line number, by the agent that holds the a11y veto. The condition the STOPP protected against (*"if the build notices that it changes an assertion"* — the build acting on its own) never occurred.

> **(b) rejected:** one day of divergence is precisely the damage variant A exists to prevent, and she names it. **(c) rejected:** §9.6 routes a Major to in-block or a follow-up PR; STOPP is for §12 classes or a finding that survived the cap and needs new prose. The remedy is a **deletion** — the cheapest, mechanically closable form.

> **Her finding is moreover substantively stronger than mine.** My #1092 ruling rested on the attribute being inert. That holds on initial mount — but not on client-nav, where the node is inserted into a live DOM with its text, which is exactly the case AT announces. Then the same sentence is out twice, one of them interrupting. And my motivation *"removing it would change the visible error's semantics for no gain"* is false in both halves: `role` carries no visual semantics, and the gain is dedup plus no interruption.

> **#1092's ErrorShell ruling is hereby overturned on this point.** Future CTO instances shall not re-derive it.

The PR body must honestly note that the `/foretag/sok` diff is no longer a pure name/path change.

### Collision 2 — her Major 1 (empty branch announces outcome without next step) → **re-bound, she is right**

`` `${t("list.emptyTitle")} ${t("list.emptyBody")}` `` — both in `jobads.ui.list`, reachable from `jobb-results.tsx`'s existing `getTranslations("jobads.ui")`. Still zero new copy.

> This is a **correction within my own principle, not against it**. I wrote that every announced sentence should be one already on screen — and then bound only `toolbar.noHits`, a *fragment* of what the screen actually shows, while the error branches in the same `switch` get cause + remedy. The enumeration contradicted the rule. The rule wins; run it, not my list.

The count branch (`totalCount > 0`) stands unchanged — there the number IS the whole message, there is no title/body pair, and she did not fell it.

### Two conditions so it costs no extra round

1. **Write no explanatory comment where `role="alert"` is struck.** A deletion closes *mechanically* only if the closing diff adds zero lines. The reason belongs in the commit message, which is not reviewed as code (§5 `Comments:`).
2. **The prose in `foretag-sok-results.tsx:236-244` explaining why `role="alert"` stays becomes factually wrong** by the strike. It is a defect (§5 `Comments:`) and is closed by **deleting** the sentences — not rewriting them. Then all of Major 3 stays a pure deletion.

Major 3 then closes mechanically with no re-check. Major 1 is a string change and needs her **one** remaining scoped re-check — which covers Major 2 and both Minors in the same delta. The cap holds.

**No severity touched, nothing filed** (§9.6; the backlog cap binds).
