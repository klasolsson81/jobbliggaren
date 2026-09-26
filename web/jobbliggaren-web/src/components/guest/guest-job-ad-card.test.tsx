import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { GuestJobAdCard } from "./guest-job-ad-card";
import type { GuestMockJobAd } from "@/lib/guest/mock-data";
import { GUEST_MOCK } from "@/lib/guest/mock-data";

const ad: GuestMockJobAd = GUEST_MOCK.jobAds[0]!;

// #1828 (senior-cto-advisor, item 3): the guest card takes the /jobb card's house form —
// the title is the only link, the rest of the card is its description.
describe("GuestJobAdCard", () => {
  it("the title is the card's only link, named by the title alone", () => {
    render(<GuestJobAdCard jobAd={ad} />);
    const link = screen.getByRole("link", { name: ad.title });
    expect(link).toHaveAttribute("href", `/gast/jobb/${ad.id}`);
    expect(link).not.toHaveAttribute("aria-label");
    expect(screen.getAllByRole("link")).toHaveLength(1);
  });

  it("the link's description carries the company, the source and both dates", () => {
    const { container } = render(<GuestJobAdCard jobAd={ad} />);
    const link = screen.getByRole("link", { name: ad.title });
    const ids = (link.getAttribute("aria-describedby") ?? "").split(" ").filter(Boolean);
    expect(ids.map((id) => document.getElementById(id))).toEqual([
      container.querySelector(".jp-job__company"),
      container.querySelector(".jp-job__meta"),
    ]);
    expect(link).toHaveAccessibleDescription(expect.stringContaining(ad.companyName));
    expect(link).toHaveAccessibleDescription(expect.stringContaining("Publicerad"));
    if (ad.expiresAtIso) {
      expect(link).toHaveAccessibleDescription(expect.stringContaining("Sista ansökningsdag"));
    }
  });
});
