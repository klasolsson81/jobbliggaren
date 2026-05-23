import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SavedJobAdList } from "./saved-job-ad-list";
import type { SavedJobAdDto } from "@/lib/dto/saved-job-ads";

const unsaveActionMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

vi.mock("@/lib/actions/saved-job-ads", () => ({
  unsaveJobAdAction: (...args: unknown[]) => unsaveActionMock(...args),
}));

function makeDto(
  id: string,
  jobAdId: string,
  title: string,
  withJobAd = true
): SavedJobAdDto {
  return {
    id,
    jobAdId,
    savedAt: "2026-05-23T15:00:00Z",
    jobAd: withJobAd
      ? {
          jobAdId,
          title,
          company: "Acme AB",
          url: "https://example.com/jobs/1",
          source: "Platsbanken",
          publishedAt: "2026-05-20T08:00:00Z",
          expiresAt: "2026-06-20T08:00:00Z",
        }
      : null,
  };
}

beforeEach(() => {
  unsaveActionMock.mockReset();
});

describe("SavedJobAdList", () => {
  it("renders empty-state when items is empty", () => {
    render(<SavedJobAdList items={[]} />);
    expect(screen.getByText("Inga sparade annonser")).toBeInTheDocument();
  });

  it("renders saved jobs with title and company", () => {
    const items = [makeDto("s1", "j1", "Backendutvecklare")];
    render(<SavedJobAdList items={items} />);
    expect(screen.getByText("Backendutvecklare")).toBeInTheDocument();
    expect(screen.getByText("Acme AB")).toBeInTheDocument();
  });

  it("renders fallback when jobAd is null (soft-deletad)", () => {
    const items = [makeDto("s1", "j1", "Borttagen", false)];
    render(<SavedJobAdList items={items} />);
    expect(screen.getByText("Annonsen är borttagen")).toBeInTheDocument();
  });

  it("optimistically removes a row on successful unsave", async () => {
    unsaveActionMock.mockResolvedValue({ success: true });
    const items = [
      makeDto("s1", "j1", "Backendutvecklare"),
      makeDto("s2", "j2", "Frontendutvecklare"),
    ];
    render(<SavedJobAdList items={items} />);

    const user = userEvent.setup();
    const removeBtn = screen.getByRole("button", {
      name: /Ta bort bokmärke för Backendutvecklare/i,
    });
    await user.click(removeBtn);

    expect(unsaveActionMock).toHaveBeenCalledWith("j1");
    expect(screen.queryByText("Backendutvecklare")).not.toBeInTheDocument();
    expect(screen.getByText("Frontendutvecklare")).toBeInTheDocument();
  });

  it("shows error and keeps row when unsave fails", async () => {
    unsaveActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte ta bort bokmärket. Försök igen.",
    });
    const items = [makeDto("s1", "j1", "Backendutvecklare")];
    render(<SavedJobAdList items={items} />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", {
        name: /Ta bort bokmärke för Backendutvecklare/i,
      })
    );

    expect(
      await screen.findByText(/Kunde inte ta bort bokmärket/i)
    ).toBeInTheDocument();
    expect(screen.getByText("Backendutvecklare")).toBeInTheDocument();
  });
});
