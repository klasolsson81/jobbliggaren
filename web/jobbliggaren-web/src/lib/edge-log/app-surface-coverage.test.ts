import { describe, expect, it } from "vitest";
import {
  PIN_LIST,
  PIN_RELATIVE,
  keysJudged,
  pinnedAppSurfaceParameters,
} from "@/test/edge-log-pin";
import { EDGE_LOG_VERDICT as AUDIT_LOG } from "@/lib/audit-log/edge-log-verdicts";
import { EDGE_LOG_VERDICT as FORETAG_SOK } from "@/lib/company-search/edge-log-verdicts";
import { EDGE_LOG_VERDICT as JOBB } from "@/lib/job-ads/edge-log-verdicts";

/**
 * The property no per-surface fact can state: **every name on the pin's array is judged by
 * somebody.**
 *
 * Each surface's own fact checks its keys against the array in both directions, but its reverse
 * direction filters to the keys that surface emits. With two names on the array and one surface
 * emitting both, that filter was a no-op and the single fact effectively covered the whole list.
 * With more names than one surface emits, the property is asserted by nobody: a name from a
 * surface that has no inventory file passes every existing fact green, which is the state the
 * pin's own docblock says is not enough (dotnet-architect, conversion trigger declared on #1593
 * and fired on #1596).
 *
 * Stated as an EQUALITY rather than a subset on purpose, so two failures fall out of one
 * assertion: a pinned name nobody judged, and a dead filter entry no surface emits any more.
 */

const JUDGED_MUST_NOT_REACH: ReadonlyArray<string> = [
  ...keysJudged(JOBB, "must-not-reach-a-stored-log-post"),
  ...keysJudged(AUDIT_LOG, "must-not-reach-a-stored-log-post"),
  ...keysJudged(FORETAG_SOK, "must-not-reach-a-stored-log-post"),
];

/**
 * Names filtered at the edge whose surface does not have an inventory file yet.
 *
 * This is a debt list, and it exists so the debt is visible rather than silent — the same job
 * `KeptParameters` does for the mail surface. An entry here means the exposure IS closed at the
 * edge; what is missing is the derivation that would keep it closed when that surface's producers
 * change. Adding one is a decision, and it should be made by writing the inventory instead
 * wherever that is possible.
 */
const AWAITING_ITS_OWN_INVENTORY: Readonly<Record<string, string>> = {
  prefix:
    "/api/jobb/suggest is a route handler, not a URL builder: its key set is what the handler " +
    "READS, which needs a different derivation from what a builder EMITS. Closed at the edge in " +
    "the same PR that found it (security-auditor, 2026-08-30) rather than left live across a PR " +
    "boundary; the inventory is owed.",
};

describe("app-surface edge-log coverage", () => {
  it("judges every parameter the pin filters, and filters every one it judges", () => {
    const accountedFor = [
      ...JUDGED_MUST_NOT_REACH,
      ...Object.keys(AWAITING_ITS_OWN_INVENTORY),
    ].sort();
    expect(
      accountedFor,
      `${PIN_RELATIVE}'s ${PIN_LIST} and the app surfaces' verdicts have diverged. A name on the ` +
        `array that no surface judges is protected by a filter nobody derives; a name judged ` +
        `must-not-reach that is not on the array is a claim with nothing behind it. Add the ` +
        `surface's inventory file, or record the debt in AWAITING_ITS_OWN_INVENTORY with a reason.`
    ).toEqual([...pinnedAppSurfaceParameters()].sort());
  });

  it("states a reason for every deferred inventory", () => {
    for (const [key, reason] of Object.entries(AWAITING_ITS_OWN_INVENTORY)) {
      expect(reason.trim().length, `"${key}" is deferred with no reason`).toBeGreaterThan(0);
    }
  });

  it("defers no name that a surface already judges", () => {
    // A key in both places would let the debt list mask a real inventory, and would keep the
    // equality above passing after the inventory that closed it was deleted.
    const both = Object.keys(AWAITING_ITS_OWN_INVENTORY).filter((k) =>
      JUDGED_MUST_NOT_REACH.includes(k)
    );
    expect(both, "these are judged by a surface and also listed as deferred").toEqual([]);
  });

  it("judges no name twice across surfaces", () => {
    // Two surfaces emitting one key is fine; two of them each declaring it must-not-reach means
    // the reason lives in two places and will drift. The equality above would still pass.
    const seen = new Set<string>();
    const duplicated = JUDGED_MUST_NOT_REACH.filter((k) => (seen.has(k) ? true : (seen.add(k), false)));
    expect(duplicated, "judged must-not-reach by more than one surface").toEqual([]);
  });
});
