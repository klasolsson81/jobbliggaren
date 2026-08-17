import { describe, it, expect, vi, beforeEach } from "vitest";
import InterceptedCvNyModal from "./page";

/**
 * @modal/(.)cv/ny — retired with the full page (#1061).
 *
 * This suite used to assert the intercept rendered `RouteModalShell` + `CreateResumeForm`.
 * It now asserts the opposite, for the reason the intercept is gated at all: one URL must have
 * ONE behaviour. Gating only the full page would leave /cv/ny answering 404 on hard-nav and
 * rendering a working create form on soft-nav.
 *
 * The intercept is unreachable today — it only fires on client-side navigation, and both
 * `<Link href="/cv/ny">` are gone — so this pin guards a re-arming, not a live path: the
 * moment anyone adds a link or a `router.push`, the gate is what stops the form coming back.
 */

const redirect = vi.fn();
const notFound = vi.fn();
const getServerSession = vi.fn();

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

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

describe("@modal/(.)cv/ny intercepting route — gated with the full page (#1061)", () => {
  beforeEach(() => {
    redirect.mockReset();
    notFound.mockReset();
    getServerSession.mockReset();
  });

  it("404s for an authenticated user instead of rendering the create modal", async () => {
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });

    await expect(InterceptedCvNyModal()).rejects.toThrow("NEXT_NOT_FOUND");

    expect(notFound).toHaveBeenCalledOnce();
    expect(redirect).not.toHaveBeenCalled();
  });

  it("redirectar till /logga-in när användaren saknar session", async () => {
    getServerSession.mockResolvedValue(null);

    await expect(InterceptedCvNyModal()).rejects.toThrow(
      "NEXT_REDIRECT:/logga-in",
    );

    expect(redirect).toHaveBeenCalledWith("/logga-in");
    expect(notFound).not.toHaveBeenCalled();
  });
});
