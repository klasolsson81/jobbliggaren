import { describe, it, expect, vi, beforeEach } from "vitest";

const { cookiesMock } = vi.hoisted(() => ({ cookiesMock: vi.fn() }));
vi.mock("next/headers", () => ({ cookies: cookiesMock }));

import {
  SETUP_WELCOMED_COOKIE,
  hasSeenSetupWelcome,
} from "./setup-welcome";
import { markSetupWelcomeSeen } from "./setup-welcome-actions";

describe("setup-welcome cookie-modul (ADR 0077 STEG 5)", () => {
  beforeEach(() => {
    cookiesMock.mockReset();
  });

  it("använder __Host--prefixad cookie-namn (paritet med session/gäst-welcome)", () => {
    expect(SETUP_WELCOMED_COOKIE).toBe("__Host-jobbliggaren_setup_welcomed");
  });

  it("hasSeenSetupWelcome → true när cookien är satt till '1'", async () => {
    cookiesMock.mockResolvedValue({
      get: (name: string) =>
        name === SETUP_WELCOMED_COOKIE ? { value: "1" } : undefined,
    });
    expect(await hasSeenSetupWelcome()).toBe(true);
  });

  it("hasSeenSetupWelcome → false när cookien saknas", async () => {
    cookiesMock.mockResolvedValue({
      get: () => undefined,
    });
    expect(await hasSeenSetupWelcome()).toBe(false);
  });

  it("hasSeenSetupWelcome → false vid annat värde än '1'", async () => {
    cookiesMock.mockResolvedValue({
      get: (name: string) =>
        name === SETUP_WELCOMED_COOKIE ? { value: "0" } : undefined,
    });
    expect(await hasSeenSetupWelcome()).toBe(false);
  });
});

describe("markSetupWelcomeSeen (Server Action)", () => {
  beforeEach(() => {
    cookiesMock.mockReset();
  });

  it("sätter cookien httpOnly/secure/sameSite=lax/path=//365d", async () => {
    const setSpy = vi.fn();
    cookiesMock.mockResolvedValue({ set: setSpy });

    await markSetupWelcomeSeen();

    expect(setSpy).toHaveBeenCalledTimes(1);
    expect(setSpy).toHaveBeenCalledWith(SETUP_WELCOMED_COOKIE, "1", {
      httpOnly: true,
      secure: true,
      sameSite: "lax",
      path: "/",
      maxAge: 365 * 24 * 60 * 60,
    });
  });
});
