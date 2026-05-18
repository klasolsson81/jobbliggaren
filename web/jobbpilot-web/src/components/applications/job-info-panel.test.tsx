import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobInfoPanel } from "./job-info-panel";
import type { JobAdSummaryDto } from "@/lib/types/applications";

function makeJobAd(overrides: Partial<JobAdSummaryDto> = {}): JobAdSummaryDto {
  return {
    jobAdId: "ad-1",
    title: "Backend-utvecklare",
    company: "Volvo",
    url: "https://example.com/ad",
    source: "Platsbanken",
    publishedAt: "2026-05-01",
    expiresAt: "2026-06-01",
    ...overrides,
  };
}

describe("JobInfoPanel", () => {
  it("renders the 'Publicerad' row when publishedAt is set", () => {
    render(<JobInfoPanel jobAd={makeJobAd()} coverLetter={null} />);

    expect(screen.getByText("Publicerad")).toBeInTheDocument();
    expect(screen.getByText("1 maj 2026")).toBeInTheDocument();
  });

  it("does NOT render the 'Publicerad' row when publishedAt is null (J1 — no CreatedAt leak)", () => {
    render(
      <JobInfoPanel
        jobAd={makeJobAd({ publishedAt: null })}
        coverLetter={null}
      />
    );

    expect(screen.queryByText("Publicerad")).not.toBeInTheDocument();
  });

  it("renders the Swedish source label ('Manuellt' for Manual)", () => {
    render(
      <JobInfoPanel
        jobAd={makeJobAd({ source: "Manual" })}
        coverLetter={null}
      />
    );

    expect(screen.getByText("Källa")).toBeInTheDocument();
    expect(screen.getByText("Manuellt")).toBeInTheDocument();
  });

  it("renders an external link with target/rel/aria-label and aria-hidden glyph when url is set", () => {
    render(
      <JobInfoPanel
        jobAd={makeJobAd({ source: "Platsbanken" })}
        coverLetter={null}
      />
    );

    const link = screen.getByRole("link", {
      name: "Visa annonsen hos Platsbanken (öppnas i ny flik)",
    });
    expect(link).toHaveAttribute("href", "https://example.com/ad");
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
    // ↗-glyfen är dekorativ
    const glyph = link.querySelector('[aria-hidden="true"]');
    expect(glyph).not.toBeNull();
    expect(glyph).toHaveTextContent("↗");
  });

  it("renders no external link when url is null", () => {
    render(
      <JobInfoPanel
        jobAd={makeJobAd({ url: null })}
        coverLetter={null}
      />
    );

    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("toggles the cover-letter disclosure and updates aria-expanded", async () => {
    const user = userEvent.setup();
    render(
      <JobInfoPanel
        jobAd={makeJobAd()}
        coverLetter="Jag söker tjänsten för att jag är väl lämpad."
      />
    );

    const toggle = screen.getByRole("button", { name: /Personligt brev/ });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(
      screen.queryByText(/Jag söker tjänsten/)
    ).not.toBeInTheDocument();

    await user.click(toggle);

    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText(/Jag söker tjänsten/)).toBeInTheDocument();

    await user.click(toggle);

    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(
      screen.queryByText(/Jag söker tjänsten/)
    ).not.toBeInTheDocument();
  });

  it("renders no disclosure when coverLetter is null", () => {
    render(<JobInfoPanel jobAd={makeJobAd()} coverLetter={null} />);

    expect(
      screen.queryByRole("button", { name: /Personligt brev/ })
    ).not.toBeInTheDocument();
  });
});
