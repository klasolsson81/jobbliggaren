import { describe, expect, it } from "vitest";
import {
  PIN_FACT,
  PIN_LIST,
  PIN_RELATIVE,
  emittedKeys,
  keysJudged as keysJudgedIn,
  pinCarriesTheCaddyfileFact,
  pinnedAppSurfaceParameters,
} from "@/test/edge-log-pin";
import { EDGE_LOG_VERDICT } from "./edge-log-verdicts";
import {
  buildJobbHref,
  buildPageHref,
  withCommitFlag,
  type JobbRawSearchParams,
  type JobbUrlState,
} from "./search-params";

/**
 * ADR 0050 gate N-1 / ADR 0087 D8(b) — the APP-surface half of the edge-log question.
 *
 * `CaddyfileTokenScrubbingPinTests` derives the MAIL surface's parameter inventory from the
 * real `EmailTemplates` methods and requires every name to be either filtered at the edge or
 * named as deliberately kept, so a new parameter fails until someone decides which it is. It
 * cannot do the same for /jobb: that inventory exists only by CALLING the TypeScript builders
 * (and `withCommitFlag`, which is where `commit` comes from and nowhere else). This file is the
 * missing derivation, and #1584 is why it is owed — that PR filtered
 * `employer` at the edge while nothing said what the route's other keys do there.
 *
 * It binds to that pin's `AppSurfaceScrubbedParameters` array and never to the Caddyfile. The
 * Caddyfile's placement is load-bearing: a `log` block in the GLOBAL options configures the
 * default logger, the one that writes `http.log.error` with the whole request line, while the
 * same lines inside the site block configure a different logger and leave that one unscrubbed.
 * A position-blind parser is the exact defect `code-reviewer` and `security-auditor` each
 * raised against an earlier version of that class, so exactly one file owns that parse.
 *
 * The rule that decides a verdict lives on `AppSurfaceScrubbedParameters` in the C# pin, because
 * it governs every app surface rather than this route: the next surface's author opens that
 * array, not another route's test file (senior-cto-advisor 2026-08-30; placement per
 * dotnet-architect).
 *
 * On the premise (CLAUDE.md §5 `Tests:`). The fixtures below are hand-built and no assertion
 * reads a fixture VALUE. What is asserted is the set of query KEY NAMES, and those are string
 * literals inside the builders: no input can change a name, input decides only whether a name
 * gets written. The values must merely clear the builders' own emission gates. The maximal
 * state is arguably not reachable through the UI, but no branch in either builder reads two
 * fields, so the union-at-once equals the union of the single-axis cases by construction of the
 * builder — and the field-to-key facts below are what keep that checkable rather than assumed.
 *
 * Residual producer, named rather than covered: `components/job-ads/jobb-hero-search.tsx`
 * renders raw hidden inputs on the no-JS form and never constructs a `JobbUrlState`, so it is a
 * third producer of these keys that nothing here measures. `lib/job-ads/company-jobs-href.ts`
 * delegates to `buildJobbHref` and is therefore not a fourth.
 */

// Annotated `Required<…>`, not `as`: an assertion would switch this control off permanently,
// and the annotation IS the guard — a field added to either type stops this file compiling
// under `tsc --noEmit`, which pre-commit and CI both run.
const FULL_STATE: Required<JobbUrlState> = {
  q: "backend",
  occupationGroup: ["og1"],
  region: ["r1"],
  municipality: ["m1"],
  remote: true,
  employmentType: ["et1"],
  worktimeExtent: ["wt1"],
  matchGrades: ["Strong"],
  matchningOff: true,
  includeRelated: true,
  hideApplied: true,
  onlyMatched: true,
  // Ten digits, or the `parseEmployerParam` gate `buildPageHref` shares with the page parser
  // drops the axis and this fixture stops being maximal.
  employer: ["5560125790"],
  sortBy: "Relevance",
  pageSize: "50",
};

const FULL_RAW_PARAMS: Required<JobbRawSearchParams> = {
  page: "9",
  pageSize: "50",
  sortBy: "Relevance",
  occupationGroup: ["og1"],
  region: ["r1"],
  municipality: ["m1"],
  employmentType: ["et1"],
  worktimeExtent: ["wt1"],
  matchGrades: ["Strong"],
  relaterade: "on",
  doljAnsokta: "on",
  baraMatchade: "on",
  distans: "on",
  employer: ["5560125790"],
  q: "backend",
};

const TARGET_PAGE = 3;
const DEFAULT_PAGE_SIZE = 20;

/** The one raw field `buildPageHref` does not read; `targetPage` supplies the key instead. */
const PAGE_FIELD = "page";

const JOBB_HREF_KEYS = emittedKeys(buildJobbHref(FULL_STATE));
const PAGE_HREF_KEYS = emittedKeys(
  buildPageHref(FULL_RAW_PARAMS, TARGET_PAGE, DEFAULT_PAGE_SIZE)
);
const COMMIT_HREF_KEYS = emittedKeys(withCommitFlag(buildJobbHref(FULL_STATE)));

const EMITTED: ReadonlySet<string> = new Set([
  ...JOBB_HREF_KEYS,
  ...PAGE_HREF_KEYS,
  ...COMMIT_HREF_KEYS,
]);

// What the kept reasons below do and do not claim (security-auditor, 2026-08-30). They describe
// the value space the UI PRODUCES, not the one the builders will re-emit: only `employer` and `q`
// are gated (`parseEmployerParam`, `parseQParam`), while `toStringList` splits, trims and drops
// empties without validating, and `pageSize`/`sortBy` are re-emitted on a `!==` check. So a
// hand-edited `?region=<anything>` does reach the edge — but that value is the visitor typing
// into their own address bar, which is a different thing from a field the app INVITES free text
// into. That difference is the whole of why `q` is scrubbed and these are not.
//
// Nor does "identifies nobody" describe the log POST. The Caddy filter names two fields,
// `request>uri query` and `request>headers`; everything else passes through, `remote_ip`
// included, and an IP is personal data (Art. 4(1), Breyer C-582/14). The kept verdicts rest on
// the value's content being bounded, never on the record being anonymous. The aggregation risk
// rides on that IP field, and its controls are the log's field set and retention, not this list.

const keysJudged = (v: "must-not-reach-a-stored-log-post" | "kept") =>
  keysJudgedIn(EDGE_LOG_VERDICT, v);

describe("/jobb axis inventory — an edge-log verdict per emitted query key", () => {
  // Both directions collect before asserting rather than failing inside the loop: a maintainer
  // who added three axes should be told about three, not made to re-run twice to find them.
  it("gives every key the builders emit a verdict", () => {
    const undecided = [...EMITTED].filter((key) => EDGE_LOG_VERDICT[key] === undefined);
    expect(
      undecided,
      `/jobb emits these keys with no edge-log verdict. Decide one each: must not reach a ` +
        `stored log post (then add it to ${PIN_LIST} and to the Caddyfile global log block), ` +
        `or kept with a written reason.`
    ).toEqual([]);
  });

  it("keeps no verdict that outlived the key it was written for", () => {
    const orphaned = Object.keys(EDGE_LOG_VERDICT).filter((key) => !EMITTED.has(key));
    expect(
      orphaned,
      `These keys carry an edge-log verdict but no builder emits them any more. A verdict over ` +
        `a key that does not exist reads as protection and is none — delete it.`
    ).toEqual([]);
  });

  it("states a reason on every verdict", () => {
    for (const [key, decision] of Object.entries(EDGE_LOG_VERDICT)) {
      expect(decision.reason.trim().length, `"${key}" has an empty reason`).toBeGreaterThan(0);
    }
  });

  it("emits a non-empty inventory of the expected size", () => {
    // Without this the three facts above pass vacuously the moment the emitted set and the
    // verdict map are emptied together. The numbers are meant to change when an axis is added:
    // re-derive the union, then give the new key a verdict.
    expect(EMITTED.size).toBeGreaterThan(0);
    expect(JOBB_HREF_KEYS.size).toBe(15);
    expect(PAGE_HREF_KEYS.size).toBe(15);
    expect(EMITTED.size).toBe(17);
    expect(keysJudged("must-not-reach-a-stored-log-post").length).toBeGreaterThan(0);
  });

  it("writes one key per state field, so a filled field cannot go unemitted", () => {
    // `Required<…>` forces a new field to be PRESENT, never to be emitted: a new list field set
    // to [] compiles and writes nothing. This is the half that catches that.
    expect(
      JOBB_HREF_KEYS.size,
      "buildJobbHref writes one key per JobbUrlState field. If a field stopped being emitted, " +
        "re-derive the union per axis — do not loosen this."
    ).toBe(Object.keys(FULL_STATE).length);

    const readFields = Object.keys(FULL_RAW_PARAMS).filter((f) => f !== PAGE_FIELD);
    const carried = [...PAGE_HREF_KEYS].filter((k) => k !== PAGE_FIELD);
    expect(carried.length).toBe(readFields.length);
    expect(PAGE_HREF_KEYS.has(PAGE_FIELD)).toBe(true);
  });

  it("adds exactly the commit key on top of a built href", () => {
    expect([...COMMIT_HREF_KEYS].filter((k) => !JOBB_HREF_KEYS.has(k))).toEqual(["commit"]);
  });

  it("partitions the inventory across both verdicts, so nothing is decided by absence", () => {
    // A keep verdict has to be DECIDED. A key merely missing from the scrubbed list is
    // indistinguishable from a key nobody looked at — the defect this file exists to close,
    // one level up. So both halves are registered and together they are the whole inventory.
    const scrubbed = keysJudged("must-not-reach-a-stored-log-post");
    const kept = keysJudged("kept");
    expect([...scrubbed, ...kept].sort()).toEqual([...EMITTED].sort());
    expect(kept.length).toBeGreaterThan(0);
  });
});

describe("the chain to the edge: this inventory to the C# pin list to the Caddyfile", () => {
  it("has every must-not-reach key on the pin app-surface list", () => {
    const pinned = pinnedAppSurfaceParameters();
    for (const key of keysJudged("must-not-reach-a-stored-log-post")) {
      expect(
        pinned,
        `"${key}" is judged must-not-reach here, but ${PIN_RELATIVE} does not list it in ` +
          `${PIN_LIST} — so nothing checks the Caddyfile actually filters it, and this verdict ` +
          `is a claim rather than a fact.`
      ).toContain(key);
    }
  });

  it("judges every pinned parameter this route emits", () => {
    const mustNotReach = keysJudged("must-not-reach-a-stored-log-post");
    for (const name of pinnedAppSurfaceParameters().filter((n) => EMITTED.has(n))) {
      expect(
        mustNotReach,
        `${PIN_RELATIVE} filters "${name}" at the edge, /jobb emits it, and this file judges it ` +
          `kept. One of the two is wrong.`
      ).toContain(name);
    }
  });

  it("still finds the pin list and the fact that binds it to the Caddyfile", () => {
    expect(pinnedAppSurfaceParameters().length).toBeGreaterThan(0);
    expect(
      pinCarriesTheCaddyfileFact(),
      `${PIN_RELATIVE} no longer carries ${PIN_FACT}. The chain third link is gone: this file ` +
        `would keep asserting against a list nobody checks the Caddyfile against.`
    ).toBe(true);
  });
});
