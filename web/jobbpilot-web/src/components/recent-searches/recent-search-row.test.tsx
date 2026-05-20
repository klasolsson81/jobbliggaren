import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RecentSearchRow } from "./recent-search-row";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";

const pushMock = vi.fn();
const deleteActionMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

vi.mock("@/lib/actions/recent-searches", () => ({
  deleteRecentSearchAction: (...args: unknown[]) => deleteActionMock(...args),
}));

function makeDto(extra?: Partial<RecentJobSearchDto>): RecentJobSearchDto {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    q: "backend",
    ssykList: ["MVqp_eS8_kDZ"],
    regionList: ["CifL_Rzy_Mku"],
    ssykLabels: [{ conceptId: "MVqp_eS8_kDZ", label: "Mjukvaruutveckling" }],
    regionLabels: [{ conceptId: "CifL_Rzy_Mku", label: "Stockholms län" }],
    sortBy: "PublishedAtDesc",
    label: "backend i Mjukvaruutveckling, Stockholms län",
    currentCount: 42,
    newCount: 0,
    lastViewedAt: "2026-05-20T19:00:00Z",
    ...extra,
  };
}

beforeEach(() => {
  pushMock.mockClear();
  deleteActionMock.mockReset();
});

describe("RecentSearchRow", () => {
  it("renders label as h3 + count meta as '(N) träffar' when newCount === 0", () => {
    render(
      <RecentSearchRow
        item={makeDto({ currentCount: 42, newCount: 0 })}
        onDeleted={() => undefined}
        onDeleteFailed={() => undefined}
      />,
    );
    expect(
      screen.getByRole("heading", { name: /backend i Mjukvaruutveckling/ }),
    ).toBeInTheDocument();
    expect(screen.getByText(/42/)).toBeInTheDocument();
    expect(screen.getByText(/träffar/)).toBeInTheDocument();
    expect(screen.queryByText(/nya/)).not.toBeInTheDocument();
  });

  it("renders 'varav (M) nya' when newCount > 0", () => {
    render(
      <RecentSearchRow
        item={makeDto({ currentCount: 42, newCount: 7 })}
        onDeleted={() => undefined}
        onDeleteFailed={() => undefined}
      />,
    );
    expect(screen.getByText(/42/)).toBeInTheDocument();
    expect(screen.getByText(/varav/)).toBeInTheDocument();
    expect(screen.getByText(/7/)).toBeInTheDocument();
    expect(screen.getByText(/nya/)).toBeInTheDocument();
  });

  it("renders NO 'NY'/'Nya'-pill (Klas-direktiv 2026-05-20 anti-AI-trope)", () => {
    const { container } = render(
      <RecentSearchRow
        item={makeDto({ newCount: 7 })}
        onDeleted={() => undefined}
        onDeleteFailed={() => undefined}
      />,
    );
    // Anti-regression: ingen separat pill-yta med "NY"-text + ingen
    // .jp-pill--success/.jp-job__newflag-klass på raden.
    expect(container.querySelector(".jp-job__newflag")).toBeNull();
    expect(container.querySelector(".jp-pill--success")).toBeNull();
    expect(screen.queryByText(/^NY$/)).not.toBeInTheDocument();
    expect(screen.queryByText(/^Nya$/)).not.toBeInTheDocument();
  });

  it("has 'Kör igen' primary action linking to a /jobb-URL built from the filter", () => {
    render(
      <RecentSearchRow
        item={makeDto()}
        onDeleted={() => undefined}
        onDeleteFailed={() => undefined}
      />,
    );
    const link = screen.getByRole("link", { name: /Kör igen/ });
    expect(link.getAttribute("href")).toMatch(/^\/jobb\?/);
    expect(link.getAttribute("href")).toContain("ssyk=MVqp_eS8_kDZ");
    expect(link.getAttribute("href")).toContain("region=CifL_Rzy_Mku");
    expect(link.getAttribute("href")).toContain("q=backend");
  });

  it("delete-button has aria-label that includes the search label", () => {
    render(
      <RecentSearchRow
        item={makeDto()}
        onDeleted={() => undefined}
        onDeleteFailed={() => undefined}
      />,
    );
    expect(
      screen.getByRole("button", { name: /Ta bort sökning/ }),
    ).toBeInTheDocument();
  });

  it("click on row body navigates via router.push to run-href", async () => {
    const user = userEvent.setup();
    render(
      <RecentSearchRow
        item={makeDto()}
        onDeleted={() => undefined}
        onDeleteFailed={() => undefined}
      />,
    );
    // Klicka på h3 inuti raden — utanför både Kör igen-länk och delete-knapp.
    await user.click(screen.getByRole("heading", { name: /backend/ }));
    expect(pushMock).toHaveBeenCalledTimes(1);
    expect(pushMock.mock.calls[0]?.[0]).toMatch(/^\/jobb\?/);
  });

  it("delete-button calls deleteRecentSearchAction and onDeleted on success", async () => {
    const user = userEvent.setup();
    deleteActionMock.mockResolvedValue({ success: true });
    const onDeleted = vi.fn();
    render(
      <RecentSearchRow
        item={makeDto()}
        onDeleted={onDeleted}
        onDeleteFailed={() => undefined}
      />,
    );
    await user.click(screen.getByRole("button", { name: /Ta bort/ }));
    expect(deleteActionMock).toHaveBeenCalledWith(
      "11111111-1111-1111-1111-111111111111",
    );
    expect(onDeleted).toHaveBeenCalledWith(
      "11111111-1111-1111-1111-111111111111",
    );
  });

  it("calls onDeleteFailed when action returns success=false", async () => {
    const user = userEvent.setup();
    deleteActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte ta bort sökningen. Försök igen.",
    });
    const onFailed = vi.fn();
    render(
      <RecentSearchRow
        item={makeDto()}
        onDeleted={() => undefined}
        onDeleteFailed={onFailed}
      />,
    );
    await user.click(screen.getByRole("button", { name: /Ta bort/ }));
    expect(onFailed).toHaveBeenCalledWith(
      "11111111-1111-1111-1111-111111111111",
      expect.stringContaining("Kunde inte"),
    );
  });
});
