import { describe, it, expect } from "vitest";
import nextConfig from "../../../../next.config";

// A change-email or delete-account challenge lives only in its section's client state, and leaving
// the section must drop it (#1740 S1, ADR 0145 D1). Next unmounts the page left behind only while
// `cacheComponents` is off: on, it keeps recently left routes mounted and hidden, state included.
describe("the /mina-sidor sections", () => {
  it("are unmounted when left, so cacheComponents stays off", () => {
    expect(nextConfig.cacheComponents).toBe(false);
  });
});
