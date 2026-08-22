import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SavedJobAdsHeroChip } from "./saved-job-ads-hero-chip";
import type { SavedJobAdDto } from "@/lib/dto/saved-job-ads";

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

describe("SavedJobAdsHeroChip", () => {
  it("borttagen annons dämpas, kvarvarande gör det inte (konsumentens isMuted-predikat)", async () => {
    const user = userEvent.setup();
    render(
      <SavedJobAdsHeroChip
        items={[makeDto({ jobAd: null }), makeDto({ id: "s2" })]}
      />,
    );
    await user.click(screen.getByRole("button", { name: /Sparade annonser/ }));
    // Both directions. A host that muted UNCONDITIONALLY passed every assertion in
    // this suite, so the negative half is what makes the predicate load-bearing.
    expect(screen.getByText("Annonsen är borttagen")).toHaveClass(
      "jp-popover__rowlabel--muted",
    );
    expect(
      screen.getByText("Systemutvecklare inom offentlig sektor"),
    ).not.toHaveClass("jp-popover__rowlabel--muted");
  });

  it("radens href pekar på annonsen, och klick stänger dropdownen", async () => {
    const user = userEvent.setup();
    render(<SavedJobAdsHeroChip items={[makeDto()]} />);
    await user.click(screen.getByRole("button", { name: /Sparade annonser/ }));
    const row = screen.getByRole("link", {
      name: /Systemutvecklare inom offentlig sektor/,
    });
    expect(row).toHaveAttribute("href", "/jobb/ad-1");
    await user.click(row);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
