import { describe, expect, it } from "vitest";
import {
  PIN_FACT,
  PIN_LIST,
  PIN_RELATIVE,
  emittedKeys,
  pinCarriesTheCaddyfileFact,
  pinnedAppSurfaceParameters,
} from "@/test/edge-log-pin";
import {
  buildAuditLogPageHref,
  type AuditLogRawSearchParams,
} from "./page-href";

/**
 * ADR 0050 gate N-1, app-surface half, for `/admin/granskning`.
 *
 * Owed because the C# pin says so: a name on `AppSurfaceScrubbedParameters` from a surface with no
 * inventory file is held by the Caddyfile fact and by nothing else. `userId` was added to that
 * array, so this file is the other half.
 *
 * Why the exposure existed at all is the thing to keep: the edge already deleted `uid`, and `uid`
 * is not `userId`. Caddy matches query keys exactly and case-sensitively, measured 2026-08-29 on
 * 2.11.4 where `?TOKEN=` passed a filter deleting `token`. A near-miss on a key name is invisible
 * from either file alone, which is the whole reason this chain is machine-bound rather than
 * remembered.
 *
 * On the premise (CLAUDE.md §5 `Tests:`). The fixture is hand-built and no assertion reads a
 * fixture VALUE. What is asserted is the set of query KEY NAMES, and those are string literals
 * inside the builder; input decides only WHETHER a name is written, never which. Each key is
 * written by exactly one field, independently of the others, which is what makes filling them all
 * at once equal to the union of the single-field cases, and the field-to-key fact below is what
 * keeps that checkable rather than assumed.
 */

type EdgeLogVerdict = {
  readonly verdict: "must-not-reach-a-stored-log-post" | "kept";
  readonly reason: string;
};

// Annotated `Required<…>`, not `as`: the annotation IS the completeness guard, so a field added to
// the type stops this file compiling under `tsc --noEmit`, which pre-commit and CI both run.
const FULL_PARAMS: Required<AuditLogRawSearchParams> = {
  page: "9",
  pageSize: "50",
  from: "2026-01-01T00:00:00Z",
  to: "2026-12-31T23:59:59Z",
  userId: "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
  eventType: "ApplicationSubmitted",
  aggregateType: "Application",
};

const TARGET_PAGE = 3;

/** The one field the builder does not read; `page` comes from the second argument instead. */
const PAGE_FIELD = "page";

const EMITTED = emittedKeys(buildAuditLogPageHref(FULL_PARAMS, TARGET_PAGE));

const EDGE_LOG_VERDICT: Readonly<Record<string, EdgeLogVerdict>> = {
  userId: {
    verdict: "must-not-reach-a-stored-log-post",
    reason:
      "A DIRECT identifier of a natural person (Art. 4(1)), and the URL discloses more than the " +
      "identifier: it says WHOSE audit records were read. The edge's existing `uid` entry does " +
      "not cover it, because the filter matches keys exactly and case-sensitively.",
  },
  from: {
    verdict: "kept",
    reason:
      "An ISO 8601 instant bounding the query window. It names a time, not a person, and the " +
      "same value is produced by anyone who picks that window.",
  },
  to: {
    verdict: "kept",
    reason: "An ISO 8601 instant, same bounded class as from.",
  },
  eventType: {
    verdict: "kept",
    reason:
      "A domain event name from a closed, enumerated set. It selects which server-side query " +
      "path ran and identifies nobody.",
  },
  aggregateType: {
    verdict: "kept",
    reason: "An aggregate name from a closed, enumerated set, same class as eventType.",
  },
  page: {
    verdict: "kept",
    reason: "A page ordinal. It carries no user content.",
  },
  pageSize: {
    verdict: "kept",
    reason: "A page-size integer. It carries no user content.",
  },
};

function keysJudged(verdict: EdgeLogVerdict["verdict"]): ReadonlyArray<string> {
  return Object.entries(EDGE_LOG_VERDICT)
    .filter(([, d]) => d.verdict === verdict)
    .map(([key]) => key);
}

describe("/admin/granskning inventory — an edge-log verdict per emitted query key", () => {
  it("gives every key the builder emits a verdict", () => {
    const undecided = [...EMITTED].filter((key) => EDGE_LOG_VERDICT[key] === undefined);
    expect(
      undecided,
      `/admin/granskning emits these keys with no edge-log verdict. Decide one each: must not ` +
        `reach a stored log post (then add it to ${PIN_LIST} and to the Caddyfile global log ` +
        `block), or kept with a written reason.`
    ).toEqual([]);
  });

  it("keeps no verdict that outlived the key it was written for", () => {
    const orphaned = Object.keys(EDGE_LOG_VERDICT).filter((key) => !EMITTED.has(key));
    expect(
      orphaned,
      `These keys carry a verdict but the builder no longer emits them. A verdict over a key ` +
        `that does not exist reads as protection and is none.`
    ).toEqual([]);
  });

  it("states a reason on every verdict", () => {
    for (const [key, decision] of Object.entries(EDGE_LOG_VERDICT)) {
      expect(decision.reason.trim().length, `"${key}" has an empty reason`).toBeGreaterThan(0);
    }
  });

  it("emits a non-empty inventory of the expected size", () => {
    // Without this the facts above pass vacuously the moment the emitted set and the verdict map
    // are emptied together. The number is meant to change when a param is added: re-derive, then
    // give the new key a verdict.
    expect(EMITTED.size).toBeGreaterThan(0);
    expect(EMITTED.size).toBe(7);
    expect(keysJudged("must-not-reach-a-stored-log-post").length).toBeGreaterThan(0);
  });

  it("writes one key per field, so a filled field cannot go unemitted", () => {
    // `Required<…>` forces a new field to be PRESENT, never to be emitted: a new optional string
    // left falsy compiles and writes nothing. This is the half that catches that.
    const readFields = Object.keys(FULL_PARAMS).filter((f) => f !== PAGE_FIELD);
    const carried = [...EMITTED].filter((k) => k !== PAGE_FIELD);
    expect(carried.length).toBe(readFields.length);
    expect(EMITTED.has(PAGE_FIELD)).toBe(true);
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
        `${PIN_RELATIVE} filters "${name}" at the edge, /admin/granskning emits it, and this ` +
          `file judges it kept. One of the two is wrong.`
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
