import { describe, expect, it } from "vitest";
import enLegal from "../../../messages/en/content-legal.json";
import svLegal from "../../../messages/sv/content-legal.json";
import * as cookieNames from "./cookie-names";

// A cookie this app sets and the cookie policy does not list is a false statement about where
// data is stored (#1738: the login flow cookie holds the typed address, and the policy's
// neighbouring line said the session cookie holds no personal data). Derived from the module's
// exports, so a new `*_COOKIE_NAME` here fails until both locales describe it.
const NAMES = Object.entries(cookieNames)
  .filter(([key]) => key.endsWith("_COOKIE_NAME"))
  .map(([, value]) => value as string);

describe("the cookies this module names", () => {
  it("are at least the four known ones (no vacuous pass)", () => {
    expect(NAMES).toEqual(
      expect.arrayContaining([
        "__Host-jobbliggaren_session",
        "__Host-jobbliggaren_refresh_after",
        "__Host-jobbliggaren_login",
        "__Host-jobbliggaren_oauth",
      ])
    );
  });

  it.each([
    ["sv", svLegal],
    ["en", enLegal],
  ] as const)("are each listed in the %s cookie policy", (_locale, legal) => {
    const listed = JSON.stringify(legal.cookies);

    for (const name of NAMES) expect(listed).toContain(`${name}:`);
  });
});
