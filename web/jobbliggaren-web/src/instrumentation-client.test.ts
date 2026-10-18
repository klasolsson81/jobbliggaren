import { describe, expect, it } from "vitest";
import { z } from "zod";

describe("instrumentation-client", () => {
  it("turns off Zod's eval probe before any client code parses", async () => {
    expect(z.config().jitless).not.toBe(true);

    await import("./instrumentation-client");

    expect(z.config().jitless).toBe(true);
  });
});
