import { describe, expect, it } from "vitest";
import { BOUNCE_MARKER } from "./bounce-marker";
import { stripBounceMarker } from "./strip-bounce-marker";

const ID = "0b0b0b0b-1111-4222-8333-444444444444";

describe("stripBounceMarker", () => {
  it("takes the marker off the address and keeps the path", () => {
    expect(stripBounceMarker(`https://jobbliggaren.se/jobb/${ID}?${BOUNCE_MARKER}=1`)).toBe(`/jobb/${ID}`);
  });

  it("keeps every other query parameter and the fragment, in order", () => {
    expect(stripBounceMarker(`https://jobbliggaren.se/jobb/${ID}?a=1&${BOUNCE_MARKER}=1&b=2#beskrivning`)).toBe(
      `/jobb/${ID}?a=1&b=2#beskrivning`
    );
  });

  it("returns null when the address carries no marker, so nothing is rewritten", () => {
    expect(stripBounceMarker(`https://jobbliggaren.se/jobb/${ID}`)).toBeNull();
    expect(stripBounceMarker(`https://jobbliggaren.se/jobb/${ID}?a=1`)).toBeNull();
  });
});
