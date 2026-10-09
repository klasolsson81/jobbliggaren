import { describe, expect, it } from "vitest";
import {
  PIN_FACT,
  PIN_LIST,
  PIN_RELATIVE,
  emittedKeys,
  keysJudged,
  pinCarriesTheCaddyfileFact,
  pinnedAppSurfaceParameters,
} from "@/test/edge-log-pin";
import { adminLoginHref } from "./admin-return";
import { externalLoginStartHref } from "./external-login";
import { EDGE_LOG_VERDICT } from "./login-next-edge-log-verdicts";

/**
 * ADR 0050 gate N-1, app-surface half, for the login flow's return path (#1979).
 *
 * The builders that put `next` on a request line are read here as producers: `adminLoginHref` for
 * `/logga-in` and `externalLoginStartHref` for the provider start. The proxy writes `/logga-in?next=` inline,
 * and `proxy.test.ts` pins that it writes that one key.
 *
 * On the premise (CLAUDE.md §5 `Tests:`): the inputs are hand-built, and what is asserted is the set of key
 * names the builders write, which are literals inside them.
 */

const FEEDBACK_RETURN = "/admin/feedback?id=3f2504e0-4f89-41d3-9a0c-0305e82c3301";

const EMITTED = new Set([
  ...emittedKeys(adminLoginHref(FEEDBACK_RETURN)),
  ...emittedKeys(externalLoginStartHref("google", FEEDBACK_RETURN)),
]);

describe("login return path inventory — an edge-log verdict per emitted query key", () => {
  it("emits next from both producers, and nothing else", () => {
    expect([...emittedKeys(adminLoginHref(FEEDBACK_RETURN))]).toEqual(["next"]);
    expect([...emittedKeys(externalLoginStartHref("google", FEEDBACK_RETURN))]).toEqual(["next"]);
  });

  it("judges every key the producers emit, and nothing they do not", () => {
    expect(Object.keys(EDGE_LOG_VERDICT).sort()).toEqual([...EMITTED].sort());
  });

  it("filters at the edge every key judged must-not-reach", () => {
    const pinned = new Set(pinnedAppSurfaceParameters());
    expect(
      keysJudged(EDGE_LOG_VERDICT, "must-not-reach-a-stored-log-post").filter((key) => !pinned.has(key)),
      `these keys are judged must-not-reach but are not on ${PIN_LIST} in ${PIN_RELATIVE}`
    ).toEqual([]);
  });

  it("keeps the pin that binds the array to the Caddyfile", () => {
    expect(pinCarriesTheCaddyfileFact(), `${PIN_RELATIVE} no longer carries ${PIN_FACT}`).toBe(true);
  });
});
