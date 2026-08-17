import { describe, it, expect, vi, beforeEach } from "vitest";
import CvDetailPage from "./page";

/**
 * /cv/[id] — the WYSIWYG edit route for a SAVED CV, retired to a session-gated 404 (#1373,
 * Klas-direktiv 2026-08-17: "inte heller kunna redigera uppladdat CV, varken som funktion
 * eller vid granskning").
 *
 * The load-bearing assertion is the 404 for an **authenticated** user. A guard that only
 * exercised the logged-out path would measure the session gate — which predates this change
 * and would pass just as green against a fully working WYSIWYG editor. Only the authenticated
 * arm can tell "paused" from "merely behind a login".
 *
 * Order matters as much as outcome: the session gate runs FIRST, so a logged-out visitor
 * lands on /logga-in and never on a 404 that would confirm the route exists. Route existence
 * is not an auth oracle either way (the ny/mall/forbattra precedent, ADR 0112 §Mechanism 1).
 *
 * The page takes `params`, unlike /cv/ny — the gate must fire without ever awaiting it, since
 * a gate that first resolved the id would be doing work on behalf of a route that answers 404.
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

const params = Promise.resolve({ id: "11111111-1111-1111-1111-111111111111" });

describe("/cv/[id] — the edit path is paused, and unreachable by a guessed URL (#1373)", () => {
  beforeEach(() => {
    redirect.mockReset();
    notFound.mockReset();
    getServerSession.mockReset();
  });

  it("404s for an AUTHENTICATED user — the WYSIWYG editor is not reachable by URL", async () => {
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });

    await expect(CvDetailPage({ params })).rejects.toThrow("NEXT_NOT_FOUND");

    expect(notFound).toHaveBeenCalledOnce();
    expect(redirect).not.toHaveBeenCalled();
  });

  it("redirects a logged-out visitor to /logga-in BEFORE the 404, leaking no route existence", async () => {
    getServerSession.mockResolvedValue(null);

    await expect(CvDetailPage({ params })).rejects.toThrow(
      "NEXT_REDIRECT:/logga-in",
    );

    expect(redirect).toHaveBeenCalledWith("/logga-in");
    expect(notFound).not.toHaveBeenCalled();
  });
});
