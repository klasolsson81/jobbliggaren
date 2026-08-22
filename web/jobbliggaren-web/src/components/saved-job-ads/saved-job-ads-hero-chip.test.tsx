import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SavedJobAdsHeroChip } from "./saved-job-ads-hero-chip";
import type { SavedJobAdDto } from "@/lib/dto/saved-job-ads";

const pushMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

function makeDto(extra: Partial<SavedJobAdDto> = {}): SavedJobAdDto {
  return {
    id: "s1",
    jobAdId: "ad-1",
    savedAt: "2026-05-20T19:00:00Z",
    jobAd: {
      jobAdId: "ad-1",
      title: "Systemutvecklare inom offentlig sektor",
      company: "Göteborgs stad",
      url: null,
      source: "Platsbanken",
      publishedAt: null,
      expiresAt: null,
      status: "Active",
    },
    ...extra,
  };
}

beforeEach(() => {
  pushMock.mockClear();
});

describe("SavedJobAdsHeroChip", () => {
  it("borttagen annons dämpas (konsumentens isMuted-predikat)", async () => {
    const user = userEvent.setup();
    render(<SavedJobAdsHeroChip items={[makeDto({ jobAd: null })]} />);
    await user.click(screen.getByRole("button", { name: /Sparade annonser/ }));
    const titleSpan = screen.getByText("Annonsen är borttagen");
    expect(titleSpan).toHaveStyle({ opacity: "0.6" });
  });

  it("klick på rad → router.push till annonsen, dropdown stänger", async () => {
    const user = userEvent.setup();
    render(<SavedJobAdsHeroChip items={[makeDto()]} />);
    await user.click(screen.getByRole("button", { name: /Sparade annonser/ }));
    await user.click(screen.getByText("Systemutvecklare inom offentlig sektor"));
    expect(pushMock).toHaveBeenCalledWith("/jobb/ad-1");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
