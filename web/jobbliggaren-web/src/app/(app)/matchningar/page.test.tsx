import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../messages/sv/pages.json";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { MatchList as MatchListData } from "@/lib/dto/me-matches";
import MatchningarPage from "./page";

const redirect = vi.fn();
const getServerSession = vi.fn();
const getSessionId = vi.fn<() => Promise<string | null>>();
const getMyMatches = vi.fn<() => Promise<ApiResult<MatchListData>>>();
const markMatchesSeen =
  vi.fn<(seenThrough?: string, session?: string | null) => Promise<ApiResult<void>>>();

// #741 — the mark-seen write is scheduled with `after()` (runs after the response is
// sent). Invoke the callback synchronously so the test still observes the scheduled
// write and pins its arguments (the render-path/off-render-path split is structural).
vi.mock("next/server", () => ({
  after: (cb: () => unknown) => {
    void cb();
  },
}));

// The async server page resolves copy via `getTranslations("pages")`. next-intl's
// server entry is unavailable in jsdom → mock to a real translator over the
// Swedish `pages` catalog (source of truth).
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "pages") =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages },
      namespace,
    }),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
  getSessionId: () => getSessionId(),
}));

vi.mock("@/lib/api/me-matches", () => ({
  getMyMatches: () => getMyMatches(),
  markMatchesSeen: (seenThrough?: string, session?: string | null) =>
    markMatchesSeen(seenThrough, session),
}));

// Real `redirect()` throws NEXT_REDIRECT to halt the render — mirror that so the
// auth-guard short-circuits exactly like production (otherwise the page would
// fall through and fetch).
vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    redirect(url);
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
}));

const item: MatchListData[number] = {
  jobAdId: "11111111-1111-1111-1111-111111111111",
  title: "Systemutvecklare",
  company: "Skatteverket",
  url: "https://example.se/ad/1",
  grade: "Top",
  createdAt: "2026-06-14T08:00:00+00:00",
  isNew: true,
};

async function renderPage() {
  const element = await MatchningarPage();
  return render(element);
}

describe("/matchningar page (ADR 0080 Vag 4 PR-5)", () => {
  beforeEach(() => {
    redirect.mockReset();
    getServerSession.mockReset();
    getSessionId.mockReset();
    getMyMatches.mockReset();
    markMatchesSeen.mockReset();
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
    getSessionId.mockResolvedValue("sess-1");
    markMatchesSeen.mockResolvedValue({ kind: "ok", data: undefined });
  });

  it("utan session → redirect till /logga-in, ingen list-hämtning", async () => {
    getServerSession.mockResolvedValue(null);
    // redirect() kastar (NEXT_REDIRECT) → page-funktionen avbryts vid grinden.
    await expect(renderPage()).rejects.toThrow("NEXT_REDIRECT");
    expect(redirect).toHaveBeenCalledWith("/logga-in");
    expect(getMyMatches).not.toHaveBeenCalled();
  });

  it("lista → renderar matchningarna OCH markerar dem sedda (mark-seen on open)", async () => {
    getMyMatches.mockResolvedValue({ kind: "ok", data: [item] });
    await renderPage();

    expect(screen.getByRole("heading", { name: "Mina matchningar" })).toBeInTheDocument();
    expect(screen.getByText("Systemutvecklare")).toBeInTheDocument();
    expect(screen.getByText("Toppmatch")).toBeInTheDocument();
    expect(screen.getByText("Ny")).toBeInTheDocument();

    // KÄRN-INVARIANT (Klas-val): mark-seen körs när vyn öppnas, EFTER hämtningen.
    expect(markMatchesSeen).toHaveBeenCalledTimes(1);
    // #477 Low: sidan skickar seen-through-fönstret (nyaste visade matchningens createdAt),
    // inte klock-nu — pinnar FE-kopplingen mot en regress tillbaka till markMatchesSeen().
    // #741: sessionen läses under render och passas in (2:a arg) — `after()` i en Server
    // Component kan inte läsa cookies.
    expect(markMatchesSeen).toHaveBeenCalledWith(item.createdAt, "sess-1");
  });

  it("tom lista → nollstate-copy OCH mark-seen körs ändå (vyn öppnades)", async () => {
    getMyMatches.mockResolvedValue({ kind: "ok", data: [] });
    await renderPage();

    expect(screen.getByText("Du har inga matchningar än")).toBeInTheDocument();
    expect(markMatchesSeen).toHaveBeenCalledTimes(1);
    // Tom lista → inget fönster → undefined (backend faller tillbaka på nu).
    expect(markMatchesSeen).toHaveBeenCalledWith(undefined, "sess-1");
  });

  it("lista ok men session-cookie borta under render → inget mark-write (defensiv after()-grind)", async () => {
    // Guesten redirectas redan av getServerSession; detta täcker kant-fallet att
    // session-cookien försvinner mellan auth-grinden och getSessionId() — då
    // schemaläggs inget write (annars vore anropet i `after()` unauthorized-brus).
    getMyMatches.mockResolvedValue({ kind: "ok", data: [item] });
    getSessionId.mockResolvedValue(null);
    await renderPage();

    expect(screen.getByText("Systemutvecklare")).toBeInTheDocument();
    expect(markMatchesSeen).not.toHaveBeenCalled();
  });

  it("fel-hämtning → fel-copy OCH mark-seen körs INTE (ohederligt att 'se' ovisat)", async () => {
    getMyMatches.mockResolvedValue({ kind: "error" });
    await renderPage();

    expect(screen.getByText("Kunde inte ladda dina matchningar")).toBeInTheDocument();
    expect(markMatchesSeen).not.toHaveBeenCalled();
  });

  it("rate-limited → civic rate-limit-copy, mark-seen körs inte", async () => {
    getMyMatches.mockResolvedValue({ kind: "rateLimited", retryAfterSeconds: 30 });
    await renderPage();

    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(screen.getByText(/För många förfrågningar/)).toBeInTheDocument();
    expect(markMatchesSeen).not.toHaveBeenCalled();
  });
});
