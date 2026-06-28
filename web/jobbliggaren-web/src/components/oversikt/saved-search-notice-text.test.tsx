import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { SavedSearchNoticeText } from "./saved-search-notice-text";

// #294 — the saved-search notice text renders the real search name immediately
// and lazily appends "N nya träffar" once the off-critical-path count
// (`/api/me/recent-searches/counts`, TD-94) resolves with new hits. Never a
// fabricated count, never a misleading "0 new hits".
describe("SavedSearchNoticeText (#294 — lazy hit count)", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("shows the no-count text with the real name before the count resolves", () => {
    // Count fetch never resolves → component stays in the no-count branch.
    vi.stubGlobal(
      "fetch",
      vi.fn(() => new Promise(() => {})),
    );

    render(<SavedSearchNoticeText searchId="s1" name="Remote / Distansjobb" />);

    expect(screen.getByText(/Din senaste sökning:/)).toBeInTheDocument();
    expect(screen.getByText("Remote / Distansjobb")).toBeInTheDocument();
    // No fabricated count while loading.
    expect(screen.queryByText(/nya träffar/)).not.toBeInTheDocument();
  });

  it("appends 'N nya träffar' once the lazy count resolves with new hits", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(
        async () =>
          new Response(
            JSON.stringify([{ id: "s1", currentCount: 12, newCount: 4 }]),
            { status: 200 },
          ),
      ),
    );

    render(<SavedSearchNoticeText searchId="s1" name="Remote / Distansjobb" />);

    await waitFor(
      () => expect(screen.getByText(/4 nya träffar/)).toBeInTheDocument(),
      { timeout: 2000 },
    );
    expect(
      screen.getByText(/sedan din senaste körning/),
    ).toBeInTheDocument();
  });

  it("keeps the no-count text when the resolved count has zero new hits", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(
          JSON.stringify([{ id: "s1", currentCount: 12, newCount: 0 }]),
          { status: 200 },
        ),
    );
    vi.stubGlobal("fetch", fetchMock);

    render(<SavedSearchNoticeText searchId="s1" name="Remote / Distansjobb" />);

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    // Even after the count resolves at 0, the honest no-count text stays.
    await waitFor(() =>
      expect(screen.getByText(/Din senaste sökning:/)).toBeInTheDocument(),
    );
    expect(screen.queryByText(/nya träffar/)).not.toBeInTheDocument();
  });

  it("ignores counts for other searches (looks up its own id)", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(
        async () =>
          new Response(
            JSON.stringify([{ id: "other", currentCount: 99, newCount: 9 }]),
            { status: 200 },
          ),
      ),
    );

    render(<SavedSearchNoticeText searchId="s1" name="Remote / Distansjobb" />);

    // The resolved count is for a different id → this notice stays no-count.
    await waitFor(() =>
      expect(screen.getByText(/Din senaste sökning:/)).toBeInTheDocument(),
    );
    expect(screen.queryByText(/9 nya träffar/)).not.toBeInTheDocument();
  });
});
