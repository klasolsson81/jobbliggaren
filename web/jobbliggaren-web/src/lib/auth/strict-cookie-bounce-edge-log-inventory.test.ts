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
import { returnPathFor } from "./return-path";
import { BOUNCE_MARKER } from "./bounce-marker";
import { hopTarget } from "./strict-cookie-bounce";
import { EDGE_LOG_VERDICT } from "./strict-cookie-bounce-edge-log-verdicts";

/**
 * ADR 0050 gate N-1, app-surface half, for the Strict-cookie bounce's hop (#2094).
 *
 * The producer is `hopTarget` over `returnPathFor`, read on the return paths that carry the most: the one admin
 * page that keeps a key, and a protected page. On the premise (CLAUDE.md §5 `Tests:`): the URLs are hand-built,
 * and what is asserted is the set of key names the builders write.
 */

const hop = (path: string) => hopTarget(returnPathFor(new URL(path, "https://app.test")) ?? "");

const EMITTED = new Set([
  ...emittedKeys(hop("/admin/feedback?id=3f2504e0-4f89-41d3-9a0c-0305e82c3301&status=ny")),
  ...emittedKeys(hop("/jobb/3f2504e0-4f89-41d3-9a0c-0305e82c3301?q=utvecklare")),
]);

describe("Strict-cookie bounce inventory — an edge-log verdict per emitted query key", () => {
  it("emits the marker, and id only where the admin return path keeps it", () => {
    expect([...EMITTED].sort()).toEqual(["id", BOUNCE_MARKER].sort());
  });

  it("judges every key the hop emits, and nothing it does not", () => {
    expect(Object.keys(EDGE_LOG_VERDICT).sort()).toEqual([...EMITTED].sort());
  });

  it("filters at the edge every key judged must-not-reach", () => {
    const pinned = new Set(pinnedAppSurfaceParameters());
    expect(
      keysJudged(EDGE_LOG_VERDICT, "must-not-reach-a-stored-log-post").filter((key) => !pinned.has(key)),
      `these keys are judged must-not-reach but are not on ${PIN_LIST} in ${PIN_RELATIVE}`
    ).toEqual([]);
  });

  it("filters nothing it judges kept", () => {
    const pinned = new Set(pinnedAppSurfaceParameters());
    expect(keysJudged(EDGE_LOG_VERDICT, "kept").filter((key) => pinned.has(key))).toEqual([]);
  });

  it("keeps the pin that binds the array to the Caddyfile", () => {
    expect(pinCarriesTheCaddyfileFact(), `${PIN_RELATIVE} no longer carries ${PIN_FACT}`).toBe(true);
  });
});
