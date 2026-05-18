import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { ApplicationRow } from "./application-row";
import type {
  ApplicationDto,
  JobAdSummaryDto,
} from "@/lib/types/applications";

// next/link renderas som <a> i jsdom utan extra mock (Next 15 client Link).

const jobAd: JobAdSummaryDto = {
  jobAdId: "ad-1",
  title: "Backend-utvecklare",
  company: "Volvo",
  url: "https://example.com/ad",
  source: "Platsbanken",
  publishedAt: "2026-05-01",
  expiresAt: "2026-06-01",
};

function makeApplication(
  overrides: Partial<ApplicationDto> = {}
): ApplicationDto {
  return {
    id: "11111111-2222-3333-4444-555555555555",
    jobSeekerId: "seeker-1",
    jobAdId: "ad-1",
    status: "Submitted",
    createdAt: "2026-05-01",
    updatedAt: "2026-05-10",
    jobAd,
    ...overrides,
  };
}

describe("ApplicationRow", () => {
  it("renders 'title — company' as primary identity when jobAd is present", () => {
    render(<ApplicationRow application={makeApplication()} />);

    expect(
      screen.getByText("Backend-utvecklare — Volvo")
    ).toBeInTheDocument();
  });

  it("falls back to 'Ansökan #<first 8 of id>' (mono) when jobAd is null", () => {
    render(
      <ApplicationRow
        application={makeApplication({ jobAd: null, jobAdId: null })}
      />
    );

    const fallback = screen.getByText("Ansökan #11111111");
    expect(fallback).toBeInTheDocument();
    expect(fallback).toHaveClass("font-mono");
    expect(
      screen.queryByText("Backend-utvecklare — Volvo")
    ).not.toBeInTheDocument();
  });

  it("renders the status as a StatusDot, not a filled pill", () => {
    render(<ApplicationRow application={makeApplication()} />);

    // StatusDot exponerar etiketten "Skickad"; pill-only-klassen (bg-token)
    // ska inte finnas på status-elementet. Dot = lägst visuell vikt (§8).
    const statusEl = screen.getByText("Skickad");
    expect(statusEl).toBeInTheDocument();
    expect(statusEl.className).not.toMatch(/rounded-pill/);
  });

  it("renders 'Sök senast <date>' when jobAd.expiresAt is set", () => {
    render(<ApplicationRow application={makeApplication()} />);

    expect(screen.getByText(/Sök senast/)).toBeInTheDocument();
    // sv-SE kort datum för 2026-06-01
    expect(screen.getByText("1 juni 2026")).toBeInTheDocument();
  });

  it("omits the 'Sök senast' line when expiresAt is null", () => {
    render(
      <ApplicationRow
        application={makeApplication({
          jobAd: { ...jobAd, expiresAt: null },
        })}
      />
    );

    expect(screen.queryByText(/Sök senast/)).not.toBeInTheDocument();
  });

  it("links the whole row to /ansokningar/<id>", () => {
    render(<ApplicationRow application={makeApplication()} />);

    const link = screen.getByRole("link");
    expect(link).toHaveAttribute(
      "href",
      "/ansokningar/11111111-2222-3333-4444-555555555555"
    );
  });
});
