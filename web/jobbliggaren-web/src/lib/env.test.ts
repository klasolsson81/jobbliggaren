import { describe, it, expect, afterEach } from "vitest";
import { env } from "./env";

/**
 * DEV-ONLY flag parsing — REMOVE BEFORE LAUNCH with the flag itself
 * (docs/runbooks/release-checklist.md 2.7).
 *
 * The two halves of this flag parse garbage DIFFERENTLY, and that asymmetry is worth
 * pinning rather than remembering. The backend binds a `bool` through a TypeConverter, so
 * `"1"` throws at boot — fail-loud, which is the house rule. This side has no such
 * mechanism, so it must fail CLOSED instead: anything that is not exactly `"true"` leaves
 * a destructive affordance unrendered.
 */
describe("env.DEV_TOOLS_RESET_ENABLED", () => {
  const original = process.env.DEV_TOOLS_RESET_ENABLED;

  afterEach(() => {
    if (original === undefined) delete process.env.DEV_TOOLS_RESET_ENABLED;
    else process.env.DEV_TOOLS_RESET_ENABLED = original;
  });

  it("is true only for the exact string true", () => {
    process.env.DEV_TOOLS_RESET_ENABLED = "true";
    expect(env.DEV_TOOLS_RESET_ENABLED).toBe(true);
  });

  it.each(["1", "yes", "TRUE", "True", " true", "", "false"])(
    "fails closed for %j",
    (value) => {
      process.env.DEV_TOOLS_RESET_ENABLED = value;
      expect(env.DEV_TOOLS_RESET_ENABLED).toBe(false);
    },
  );

  it("fails closed when the variable is absent", () => {
    delete process.env.DEV_TOOLS_RESET_ENABLED;
    expect(env.DEV_TOOLS_RESET_ENABLED).toBe(false);
  });
});
