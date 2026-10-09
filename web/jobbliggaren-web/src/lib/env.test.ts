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

/**
 * #1979 PR3 — the app version the feedback BFF stamps. The backend refuses a submission whose version
 * is present but malformed (`Feedback.AppVersionInvalid`, `^[0-9a-f]{7,40}\z`) and stores an absent
 * one as unknown, so anything outside that shape must read as absent here.
 */
describe("env.APP_VERSION", () => {
  const original = process.env.APP_VERSION;

  afterEach(() => {
    if (original === undefined) delete process.env.APP_VERSION;
    else process.env.APP_VERSION = original;
  });

  it.each(["f6d917bd51a61729a4433bb34cde5180d47615f2", "f6d917b", " f6d917bd5\n"])("keeps the commit hash %j", (value) => {
    process.env.APP_VERSION = value;
    expect(env.APP_VERSION).toBe(value.trim());
  });

  it.each(["", "dev", "F6D917B", "f6d917", "f6d917bd51a61729a4433bb34cde5180d47615f2a", "f6d917b x"])(
    "reads %j as absent",
    (value) => {
      process.env.APP_VERSION = value;
      expect(env.APP_VERSION).toBeNull();
    },
  );

  it("is absent when the variable is", () => {
    delete process.env.APP_VERSION;
    expect(env.APP_VERSION).toBeNull();
  });
});
