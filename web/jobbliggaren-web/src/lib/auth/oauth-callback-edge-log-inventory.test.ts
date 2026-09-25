import { describe, expect, it } from "vitest";
import {
  PIN_FACT,
  PIN_LIST,
  PIN_RELATIVE,
  keysJudged,
  pinCarriesTheCaddyfileFact,
  pinnedAppSurfaceParameters,
} from "@/test/edge-log-pin";
import { EDGE_LOG_VERDICT } from "./oauth-callback-edge-log-verdicts";

/**
 * ADR 0050 gate N-1, app-surface half, for the external-login callback (#1744; test-writer Major 9).
 * The callback's request line carries the provider's query, and on any 5xx Caddy's default logger
 * writes the whole request line. The route answers 200 on every branch, and this inventory closes
 * the edge besides.
 */

const MUST_NOT_REACH = keysJudged(EDGE_LOG_VERDICT, "must-not-reach-a-stored-log-post");

describe("OAuth callback inventory — an edge-log verdict per provider key", () => {
  it("filters at the edge every key judged must-not-reach", () => {
    const pinned = new Set(pinnedAppSurfaceParameters());
    expect(
      MUST_NOT_REACH.filter((key) => !pinned.has(key)),
      `these callback keys are judged must-not-reach but are not on ${PIN_LIST} in ${PIN_RELATIVE}`
    ).toEqual([]);
  });

  it("filters nothing it judges kept", () => {
    const pinned = new Set(pinnedAppSurfaceParameters());
    expect(keysJudged(EDGE_LOG_VERDICT, "kept").filter((key) => pinned.has(key))).toEqual([]);
  });

  it("keeps the pin that binds the array to the Caddyfile", () => {
    expect(pinCarriesTheCaddyfileFact(), `${PIN_RELATIVE} no longer carries ${PIN_FACT}`).toBe(true);
  });

  it("judges the two credentials the callback always carries", () => {
    expect(MUST_NOT_REACH).toEqual(expect.arrayContaining(["code", "state"]));
  });
});
