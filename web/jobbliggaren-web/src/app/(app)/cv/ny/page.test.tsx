import { describe, it, expect, vi, beforeEach } from "vitest";
import NyCvPage from "./page";

/**
 * /cv/ny — the create-from-scratch route, retired to a session-gated 404 (#1061).
 *
 * The load-bearing assertion is the 404 for an **authenticated** user. A guard that only
 * exercised the logged-out path would measure the session gate — which predates this change
 * and would pass just as green against a fully working create form. Only the authenticated
 * arm can tell "deferred" from "merely behind a login".
 *
 * Order matters as much as outcome: the session gate runs FIRST, so a logged-out visitor
 * lands on /logga-in and never on a 404 that would confirm the route exists. Route existence
 * is not an auth oracle either way (the mall/forbattra precedent, ADR 0112 §Mechanism 1).
 */

const redirect = vi.fn();
const notFound = vi.fn();
const getServerSession = vi.fn();

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

// Both control-flow functions throw in production (they never return). Modelling that here is
// what lets the test assert ORDER: if the gate did not run first, the logged-out case would
// reach notFound() before redirect() ever fired.
vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    redirect(url);
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
  notFound: () => {
    notFound();
    throw new Error("NEXT_NOT_FOUND");
  },
}));

describe("/cv/ny — deferred, and unreachable by a guessed URL (#1061)", () => {
  beforeEach(() => {
    redirect.mockReset();
    notFound.mockReset();
    getServerSession.mockReset();
  });

  it("404s for an AUTHENTICATED user — the create form is not reachable by URL", async () => {
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });

    await expect(NyCvPage()).rejects.toThrow("NEXT_NOT_FOUND");

    expect(notFound).toHaveBeenCalledOnce();
    expect(redirect).not.toHaveBeenCalled();
  });

  it("redirects a logged-out visitor to /logga-in BEFORE the 404, leaking no route existence", async () => {
    getServerSession.mockResolvedValue(null);

    await expect(NyCvPage()).rejects.toThrow("NEXT_REDIRECT:/logga-in");

    expect(redirect).toHaveBeenCalledWith("/logga-in");
    expect(notFound).not.toHaveBeenCalled();
  });
});
