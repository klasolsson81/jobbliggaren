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
  buildForetagSokHref,
  buildOrgNrRefusedHref,
  buildPageHref,
  type ForetagSokUrlState,
} from "./search-params";

/**
 * ADR 0050 gate N-1, app-surface half, for `/foretag/sok`.
 *
 * `security-auditor` measured this module as a third builder module on the app surface with no
 * inventory of its own, while the C# pin's array had begun to read as app-wide. `namn` was added
 * to that array; this file is the other half, and it is the reason the array's claim is now true
 * rather than aspirational.
 *
 * `namn` is the sharp one, and its shape is worth keeping written down: the field is unbounded
 * free text, and `proxy.ts` washes an org.nr-shaped value out of it via a redirect. That wash is
 * the app STATING that it expects org.nr there, and for an enskild firma the org.nr IS the
 * holder's personnummer (#841). The wash is a 3xx on the incoming request, so it protects the
 * request AFTER the one that carried the value; that request's log post is already written.
 *
 * On the premise (CLAUDE.md §5 `Tests:`). The fixtures are hand-built and no assertion reads a
 * fixture VALUE. What is asserted is the set of query KEY NAMES, which are string literals inside
 * the builders; input decides only WHETHER a name is written. The values need only clear the
 * builders' own emission gates (a non-empty name, non-empty axes, a target page above 1).
 */

type EdgeLogVerdict = {
  readonly verdict: "must-not-reach-a-stored-log-post" | "kept";
  readonly reason: string;
};

// Annotated `Required<…>`, not `as`: the annotation IS the completeness guard, so a field added to
// `ForetagSokUrlState` stops this file compiling under `tsc --noEmit`.
const FULL_STATE: Required<ForetagSokUrlState> = {
  namn: "Byggbolaget",
  sni: ["41200"],
  kommun: ["0180"],
};

const TARGET_PAGE = 3;

const SOK_KEYS = emittedKeys(buildForetagSokHref(FULL_STATE));
const PAGE_KEYS = emittedKeys(buildPageHref(FULL_STATE, TARGET_PAGE));
// The third builder, and the only producer of the refusal flag. It takes the state WITHOUT a name
// by construction, which is the point of it.
const REFUSED_KEYS = emittedKeys(buildOrgNrRefusedHref({ sni: FULL_STATE.sni, kommun: FULL_STATE.kommun }));

const EMITTED: ReadonlySet<string> = new Set([...SOK_KEYS, ...PAGE_KEYS, ...REFUSED_KEYS]);

const EDGE_LOG_VERDICT: Readonly<Record<string, EdgeLogVerdict>> = {
  namn: {
    verdict: "must-not-reach-a-stored-log-post",
    reason:
      "Unbounded free text, the same class as /jobb's q. Worse in one respect: proxy.ts washes " +
      "an org.nr-shaped value out of this field, which is the app stating that it EXPECTS org.nr " +
      "here, and for an enskild firma the org.nr IS the holder's personnummer (#841). The wash " +
      "is a 3xx on the incoming request, whose log post is already written.",
  },
  sni: {
    verdict: "kept",
    reason:
      "SNI branch codes from the public SCB taxonomy, joined on one axis. A closed published " +
      "value space that names an industry, never a person.",
  },
  kommun: {
    verdict: "kept",
    reason: "Municipality codes from the public taxonomy, same closed published class as sni.",
  },
  sida: {
    verdict: "kept",
    reason: "A page ordinal. It carries no user content.",
  },
  avvisat: {
    verdict: "kept",
    reason:
      "A single sentinel recording that an org.nr-shaped name was refused. One bit, and it is " +
      "set precisely when the value that triggered it was NOT carried forward.",
  },
};

function keysJudged(verdict: EdgeLogVerdict["verdict"]): ReadonlyArray<string> {
  return Object.entries(EDGE_LOG_VERDICT)
    .filter(([, d]) => d.verdict === verdict)
    .map(([key]) => key);
}

describe("/foretag/sok inventory — an edge-log verdict per emitted query key", () => {
  it("gives every key the builders emit a verdict", () => {
    const undecided = [...EMITTED].filter((key) => EDGE_LOG_VERDICT[key] === undefined);
    expect(
      undecided,
      `/foretag/sok emits these keys with no edge-log verdict. Decide one each: must not reach a ` +
        `stored log post (then add it to ${PIN_LIST} and to the Caddyfile global log block), or ` +
        `kept with a written reason.`
    ).toEqual([]);
  });

  it("keeps no verdict that outlived the key it was written for", () => {
    const orphaned = Object.keys(EDGE_LOG_VERDICT).filter((key) => !EMITTED.has(key));
    expect(
      orphaned,
      `These keys carry a verdict but no builder emits them. A verdict over a key that does not ` +
        `exist reads as protection and is none.`
    ).toEqual([]);
  });

  it("states a reason on every verdict", () => {
    for (const [key, decision] of Object.entries(EDGE_LOG_VERDICT)) {
      expect(decision.reason.trim().length, `"${key}" has an empty reason`).toBeGreaterThan(0);
    }
  });

  it("emits a non-empty inventory of the expected size", () => {
    expect(EMITTED.size).toBeGreaterThan(0);
    expect(SOK_KEYS.size).toBe(3);
    expect(PAGE_KEYS.size).toBe(4);
    expect(EMITTED.size).toBe(5);
    expect(keysJudged("must-not-reach-a-stored-log-post").length).toBeGreaterThan(0);
  });

  it("writes one key per state field, so a filled field cannot go unemitted", () => {
    // `Required<…>` forces presence, never emission: a new list field set to [] compiles and
    // writes nothing. This is the half that catches that.
    expect(SOK_KEYS.size).toBe(Object.keys(FULL_STATE).length);
  });

  it("never carries the name on the refusal path", () => {
    // The refusal builder exists to produce a URL WITHOUT a name; if it ever did, the redirect
    // meant to wash an org.nr-shaped value would re-emit it into the very log post it avoids.
    expect(REFUSED_KEYS.has("namn")).toBe(false);
    expect(REFUSED_KEYS.has("avvisat")).toBe(true);
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
        `${PIN_RELATIVE} filters "${name}" at the edge, /foretag/sok emits it, and this file ` +
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
