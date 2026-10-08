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
import { EDGE_LOG_VERDICT } from "./feedback-edge-log-verdicts";
import { FEEDBACK_ROUTE, feedbackHref, parseFeedbackQuery, type AdminFeedbackQuery } from "./feedback";

/**
 * ADR 0050 gate N-1, app-surface half, for `/admin/feedback` (#1979).
 *
 * The page is driven by its URL, and every link on it is written by `feedbackHref`: the status
 * filter, the page filter and its lift, the pager, the windows, the list's rows and "Alla inskick".
 * So the builder is the page's one producer, and its key set is derived here from a query with
 * every field set away from its default, which is what makes the builder write every key it has.
 *
 * Residual producer, named rather than covered: the notice mail's link
 * (`EmailTemplates.Feedback.cs`) is written in C# and carries `id` alone, a key this file judges.
 * Nothing here would notice that template gaining a key.
 *
 * On the premise (CLAUDE.md §5 `Tests:`). The query is hand-built and no assertion reads a value
 * back out of the URL. What is asserted is the set of query KEY NAMES, which are string literals
 * inside the builder; a field decides only WHETHER its key is written, and each field writes one.
 */

// Annotated, not cast: a field added to `AdminFeedbackQuery` stops this file compiling until it is
// set here, under `tsc --noEmit`, which pre-commit and CI both run.
const FULL_QUERY: AdminFeedbackQuery = {
  status: "inProgress",
  page: "cv-review",
  pageNumber: 3,
  id: "3f2504e0-4f89-41d3-9a0c-0305e82c3301",
  window: 7,
};

const EMITTED = emittedKeys(feedbackHref(FEEDBACK_ROUTE, FULL_QUERY));

const keysJudged = (v: "must-not-reach-a-stored-log-post" | "kept") => keysJudgedIn(EDGE_LOG_VERDICT, v);

describe("/admin/feedback inventory — an edge-log verdict per emitted query key", () => {
  it("gives every key the builder emits a verdict", () => {
    const undecided = [...EMITTED].filter((key) => EDGE_LOG_VERDICT[key] === undefined);
    expect(
      undecided,
      `/admin/feedback emits these keys with no edge-log verdict. Decide one each: must not reach a ` +
        `stored log post (then add it to ${PIN_LIST} and to the Caddyfile global log block), or ` +
        `kept with a written reason.`
    ).toEqual([]);
  });

  it("keeps no verdict that outlived the key it was written for", () => {
    const orphaned = Object.keys(EDGE_LOG_VERDICT).filter((key) => !EMITTED.has(key));
    expect(
      orphaned,
      `These keys carry a verdict but the builder no longer emits them. A verdict over a key that ` +
        `does not exist reads as protection and is none.`
    ).toEqual([]);
  });

  it("states a reason on every verdict", () => {
    for (const [key, decision] of Object.entries(EDGE_LOG_VERDICT)) {
      expect(decision.reason.trim().length, `"${key}" has an empty reason`).toBeGreaterThan(0);
    }
  });

  it("emits a non-empty inventory of the expected size", () => {
    // The number is meant to change when a key is added: re-derive, then give the new key a verdict.
    expect(EMITTED.size).toBe(5);
    expect(keysJudged("must-not-reach-a-stored-log-post").length).toBeGreaterThan(0);
  });

  it("writes one key per query field, so a filled field cannot go unemitted", () => {
    // The annotation forces a new field to be PRESENT, never to be emitted: a field the builder
    // does not write compiles and writes nothing. This is the half that catches that.
    expect(EMITTED.size).toBe(Object.keys(FULL_QUERY).length);
  });

  it("reads back every key it writes, so no key rides the URL unread", () => {
    const href = feedbackHref(FEEDBACK_ROUTE, FULL_QUERY);
    expect(parseFeedbackQuery(Object.fromEntries(new URL(href, "https://example.test").searchParams))).toEqual(
      FULL_QUERY
    );
  });

  it("never writes an id the URL carried in another shape", () => {
    // The one must-not-reach key is the one the URL could carry anything in: a value that is not a
    // GUID is read as no id, so the page's own links never repeat it.
    const query = parseFeedbackQuery({ id: "namn.efternamn@example.test", status: "ny" });
    expect(emittedKeys(feedbackHref(FEEDBACK_ROUTE, query)).has("id")).toBe(false);
  });

  it("partitions the inventory across both verdicts, so nothing is decided by absence", () => {
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
          `${PIN_LIST}, so nothing checks the Caddyfile actually filters it.`
      ).toContain(key);
    }
  });

  it("judges every pinned parameter this route emits", () => {
    const mustNotReach = keysJudged("must-not-reach-a-stored-log-post");
    for (const name of pinnedAppSurfaceParameters().filter((n) => EMITTED.has(n))) {
      expect(
        mustNotReach,
        `${PIN_RELATIVE} filters "${name}" at the edge, /admin/feedback emits it, and this file ` +
          `judges it kept. One of the two is wrong.`
      ).toContain(name);
    }
  });

  it("still finds the pin list and the fact that binds it to the Caddyfile", () => {
    expect(pinnedAppSurfaceParameters().length).toBeGreaterThan(0);
    expect(
      pinCarriesTheCaddyfileFact(),
      `${PIN_RELATIVE} no longer carries ${PIN_FACT}. The chain third link is gone.`
    ).toBe(true);
  });
});
