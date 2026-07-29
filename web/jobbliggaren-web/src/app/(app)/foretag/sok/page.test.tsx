import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../../messages/sv/pages.json";
import ForetagSokPage from "./page";

const redirect = vi.fn();
const getServerSession = vi.fn();
const getCriterionReference = vi.fn();

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "pages") =>
    createTranslator({ locale: "sv", messages: { pages: svPages }, namespace }),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

vi.mock("@/lib/api/company-criteria", () => ({
  getCriterionReference: () => getCriterionReference(),
}));

// The page's two heavy children are irrelevant to what this suite pins (the gate and the refusal
// panel), and the results child is an async Server Component jsdom cannot render. Stub both so the
// page's OWN output is what is asserted.
vi.mock("@/components/company-criteria/foretag-sok-searchbar", () => ({
  ForetagSokSearchbar: () => <div data-testid="searchbar" />,
}));
vi.mock("@/components/company-criteria/foretag-sok-results", () => ({
  ForetagSokResults: () => <div data-testid="results" />,
}));

// The real `redirect()` throws NEXT_REDIRECT to halt the render — mirror that, so the gate
// short-circuits exactly as it does in production instead of falling through and rendering.
vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    redirect(url);
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
}));

function renderPage(params: Record<string, string | string[] | undefined>) {
  return ForetagSokPage({ searchParams: Promise.resolve(params) });
}

/**
 * ADR 0087 D8(c) — the org.nr gate at its CALL SITE.
 *
 * `parseNamn` returning `{ kind: "orgNrShaped" }` is pinned in `search-params.test.ts`, and the form
 * not carrying the draft is pinned in `foretag-sok-searchbar.test.tsx`. Neither of those can see
 * whether the PAGE acts on the refusal. This suite pins that: it is the only layer all three client
 * states pass through (JS disabled, Enter before hydration, a hand-typed or shared URL), so if the
 * redirect is ever removed the guard is gone for every one of them at once.
 */
describe("/foretag/sok — the org.nr gate on the URL axis", () => {
  beforeEach(() => {
    redirect.mockReset();
    getServerSession.mockReset();
    getCriterionReference.mockReset();
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
    getCriterionReference.mockResolvedValue({
      kind: "ok",
      data: { sniVersion: "2025", kommunVersion: "2025", sni: [], lan: [] },
    });
  });

  it("washes a ten-digit ?namn= out of the URL and never renders the search", async () => {
    await expect(renderPage({ namn: "1010101010" })).rejects.toThrow("NEXT_REDIRECT");

    const target = redirect.mock.calls[0]?.[0] as string;
    expect(target).toBe("/foretag/sok?avvisat=orgnr");
    // The value is gone from the target — this is what keeps it out of the address bar, out of
    // history, and out of a re-shared link.
    expect(target).not.toContain("1010101010");
  });

  it("refuses BEFORE the reference is fetched — a redirecting request does no upstream work", async () => {
    await expect(renderPage({ namn: "5560125790" })).rejects.toThrow("NEXT_REDIRECT");
    expect(getCriterionReference).not.toHaveBeenCalled();
  });

  it("preserves the filter axes and drops sida when it washes the name", async () => {
    await expect(
      renderPage({
        namn: "101010-1010",
        sni: ["62020", "62010"],
        kommun: "0180",
        sida: "4",
      }),
    ).rejects.toThrow("NEXT_REDIRECT");

    const target = redirect.mock.calls[0]?.[0] as string;
    // The wash re-serialises through the shared builder, so it emits the JOINED form — while
    // still reading the repeated form this request sends, which is what a link shared before
    // 2026-07-29 carries.
    expect(target).toContain("sni=62010-62020");
    expect(target).toContain("kommun=0180");
    expect(target).toContain("avvisat=orgnr");
    // `sida` is dropped deliberately: removing the name changes the result set, so a page number
    // from the old one can be out of range.
    expect(target).not.toContain("sida");
    expect(target).not.toContain("namn");
  });

  it("does not redirect on an ordinary name prefix", async () => {
    await renderPage({ namn: "Volvo" });
    expect(redirect).not.toHaveBeenCalled();
    expect(getCriterionReference).toHaveBeenCalled();
  });

  /**
   * The no-loop pin at the call site. The wash target is fed straight back in: it must render, not
   * redirect again. A gate that refuses its own escape hatch is an infinite redirect.
   */
  it("renders the wash target itself rather than redirecting again", async () => {
    await renderPage({ avvisat: "orgnr" });
    expect(redirect).not.toHaveBeenCalled();
  });

  it("still redirects an unauthenticated request to /logga-in", async () => {
    getServerSession.mockResolvedValue(null);
    await expect(renderPage({ namn: "1010101010" })).rejects.toThrow("NEXT_REDIRECT");
    expect(redirect).toHaveBeenCalledWith("/logga-in");
  });
});

/**
 * The refusal must be EXPLAINED, not washed silently (CTO bind §3.2): answering a specific typed
 * query with the whole register reads as "everything matched", which is the silent-drop class the
 * search island was built to eliminate. The URL-level tests above cannot see the panel — delete the
 * JSX and they all stay green — so this renders the page's own output.
 */
describe("/foretag/sok — the refusal is explained on the wash target", () => {
  beforeEach(() => {
    redirect.mockReset();
    getServerSession.mockReset();
    getCriterionReference.mockReset();
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
    getCriterionReference.mockResolvedValue({
      kind: "ok",
      data: { sniVersion: "2025", kommunVersion: "2025", sni: [], lan: [] },
    });
  });

  it("renders the refusal panel when the flag is present", async () => {
    render(await renderPage({ avvisat: "orgnr" }));

    expect(screen.getByText("Numret togs bort ur adressen")).toBeInTheDocument();
    const body = screen.getByText(/Organisationsnummer hamnar aldrig i webbadressen/);
    expect(body).toBeInTheDocument();
    // Binding copy constraint: never accuse the user of typing a personnummer — the gate covers the
    // whole ten-digit class, so the word would be wrong for a legitimate company number and would
    // advertise the heuristic. (The "never echo the value" constraint is NOT assertable here and a
    // check for it would be vacuous by construction: the page redirects, so a refused value and this
    // panel can never occupy the same request. The real pin for that is the redirect target itself,
    // asserted above.)
    expect(body.textContent).not.toMatch(/personnummer/i);
  });

  it("does not render the refusal panel on an ordinary search", async () => {
    render(await renderPage({ namn: "Volvo" }));
    expect(
      screen.queryByText("Numret togs bort ur adressen"),
    ).not.toBeInTheDocument();
  });
});
