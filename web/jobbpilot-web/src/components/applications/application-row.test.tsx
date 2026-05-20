import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { ApplicationRow } from "./application-row";
import type {
  ApplicationDto,
  JobAdSummaryDto,
} from "@/lib/types/applications";

// next/link renderas som <a> i jsdom utan extra mock (Next client Link).

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

describe("ApplicationRow (v3 .jp-app)", () => {
  it("emitterar det DELADE jp-app-radchassit (jp-job≡jp-app, HANDOVER §9)", () => {
    render(<ApplicationRow application={makeApplication()} />);
    const link = screen.getByRole("link");
    expect(link).toHaveClass("jp-app");
  });

  it("renderar EXAKT 2 grid-barn (body + actions) utan statusbadge (F5 B1, prototyp-exakt)", () => {
    const { container } = render(
      <ApplicationRow application={makeApplication()} />
    );
    const link = screen.getByRole("link");
    // Prototyp pages.jsx ApplicationRow = exakt 2 grid-barn:
    // .jp-job__body + .jp-app__actions. INGEN .jp-app__statusbadge i raden
    // (den 56px-badgen hör till modalen/detaljen).
    expect(link.children).toHaveLength(2);
    expect(link.children[0]).toHaveClass("jp-job__body");
    expect(link.children[1]).toHaveClass("jp-app__actions");
    expect(
      container.querySelector(".jp-app__statusbadge")
    ).toBeNull();
  });

  it("renders jobtitel + företag separat när jobAd finns", () => {
    render(<ApplicationRow application={makeApplication()} />);
    expect(screen.getByText("Backend-utvecklare")).toBeInTheDocument();
    expect(screen.getByText("Volvo")).toBeInTheDocument();
  });

  it("faller tillbaka till mono 'Ansökan #<8>' när jobAd är null", () => {
    render(
      <ApplicationRow
        application={makeApplication({ jobAd: null, jobAdId: null })}
      />
    );
    const fallback = screen.getByText("Ansökan #11111111");
    expect(fallback).toBeInTheDocument();
    expect(fallback).toHaveClass("jp-mono");
    expect(
      screen.queryByText("Backend-utvecklare")
    ).not.toBeInTheDocument();
  });

  it("renderar status som fylld v3-pill med dot (jp-pill--{variant})", () => {
    render(<ApplicationRow application={makeApplication()} />);
    // Submitted → Brand → "Skickad"
    const pill = screen.getByText("Skickad").closest(".jp-pill");
    expect(pill).not.toBeNull();
    expect(pill).toHaveClass("jp-pill--brand");
  });

  it("renderar 'Sök senast <date>' när jobAd.expiresAt finns", () => {
    render(<ApplicationRow application={makeApplication()} />);
    expect(screen.getByText(/Sök senast/)).toBeInTheDocument();
    expect(screen.getByText("1 juni 2026")).toBeInTheDocument();
  });

  it("utelämnar 'Sök senast'-raden när expiresAt är null", () => {
    render(
      <ApplicationRow
        application={makeApplication({
          jobAd: { ...jobAd, expiresAt: null },
        })}
      />
    );
    expect(screen.queryByText(/Sök senast/)).not.toBeInTheDocument();
  });

  it("renderar kort-id (#8) i meta-raden", () => {
    render(<ApplicationRow application={makeApplication()} />);
    expect(screen.getByText("#11111111")).toBeInTheDocument();
  });

  it("länkar hela raden till /ansokningar/<id> (intercept → modal)", () => {
    render(<ApplicationRow application={makeApplication()} />);
    const link = screen.getByRole("link");
    expect(link).toHaveAttribute(
      "href",
      "/ansokningar/11111111-2222-3333-4444-555555555555"
    );
  });

  it("har en tillgänglig aria-label med titel, företag och status", () => {
    render(<ApplicationRow application={makeApplication()} />);
    expect(
      screen.getByRole("link", {
        name: "Backend-utvecklare – Volvo – Skickad",
      })
    ).toBeInTheDocument();
  });
});
