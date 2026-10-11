import { describe, it, expect, afterEach } from "vitest";
import { env } from "./env";

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
