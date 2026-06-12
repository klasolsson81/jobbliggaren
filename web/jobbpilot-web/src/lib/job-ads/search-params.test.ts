import { describe, it, expect } from "vitest";
import {
  buildJobbHref,
  withCommitFlag,
  COMMIT_PARAM,
  type JobbUrlState,
} from "./search-params";

const empty: JobbUrlState = {
  q: "",
  occupationGroup: [],
  region: [],
  municipality: [],
  sortBy: "PublishedAtDesc",
};

describe("withCommitFlag (E2j commit-intent-signal)", () => {
  it("adderar ?commit=1 på en href utan query", () => {
    expect(withCommitFlag("/jobb")).toBe(`/jobb?${COMMIT_PARAM}=1`);
  });

  it("adderar &commit=1 på en href som redan har query", () => {
    expect(withCommitFlag("/jobb?q=volvo")).toBe(
      `/jobb?q=volvo&${COMMIT_PARAM}=1`,
    );
  });

  it("commit-flaggan ingår ALDRIG i buildJobbHref (utanför JobbUrlState)", () => {
    // Invariant (CTO VAL 5 väg 2): commit är en transient signal, inte ett
    // tillstånd — buildJobbHref emitterar den aldrig.
    expect(buildJobbHref({ ...empty, q: "volvo" })).toBe("/jobb?q=volvo");
    expect(buildJobbHref(empty)).toBe("/jobb");
  });
});
