import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobAdCard } from "./job-ad-card";
import type { JobAdDto } from "@/lib/dto/job-ads";

// publishedAt avsiktligt > 7 dygn sedan så freshness-taggen INTE renderas i
// default-tester (skulle annars läggas till h3:s accessible name och bryta
// `getByRole("heading", { name: "..." })`-assertions).
const baseAd: JobAdDto = {
  id: "11111111-1111-1111-1111-111111111111",
  title: "Senior Backend Developer",
  companyName: "Acme AB",
  description: "Vi söker en .NET-utvecklare för långsiktigt uppdrag.",
  url: "https://example.com/jobb/123",
  source: "Platsbanken",
  status: "Active",
  publishedAt: "2026-04-01T08:00:00Z",
  expiresAt: "2026-06-13T08:00:00Z",
  createdAt: "2026-04-01T08:01:00Z",
};

describe("JobAdCard (v3 .jp-job-rad)", () => {
  it("renders title and company", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(
      screen.getByRole("heading", { name: "Senior Backend Developer" })
    ).toBeInTheDocument();
    expect(screen.getByText("Acme AB")).toBeInTheDocument();
  });

  it("renders the whole row as a link to /jobb/[id]", () => {
    render(<JobAdCard jobAd={baseAd} />);
    const link = screen.getByRole("link", {
      name: "Senior Backend Developer – Acme AB",
    });
    expect(link).toHaveAttribute("href", `/jobb/${baseAd.id}`);
  });

  it("renders source label and published date in meta", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(screen.getByText("Platsbanken")).toBeInTheDocument();
    expect(screen.getByText(/Publicerad/)).toBeInTheDocument();
  });

  it("omits sista ansökan when expiresAt is null", () => {
    render(<JobAdCard jobAd={{ ...baseAd, expiresAt: null }} />);
    expect(screen.queryByText(/Sista ansökan/)).not.toBeInTheDocument();
  });

  it("renders sista ansökan when expiresAt is set", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(screen.getByText(/Sista ansökan/)).toBeInTheDocument();
  });

  // NY = oläst (#293/#306): driven av `isNew`-propen (beräknad i JobbResults
  // mot oläst-watermarken), INTE av ett borttaget JobAdDto.isNew-fält.
  it("does not render the Ny flag without the isNew prop (default false)", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(screen.queryByText("Ny")).not.toBeInTheDocument();
  });

  it("renders the Ny flag when isNew prop is true", () => {
    render(<JobAdCard jobAd={baseAd} isNew={true} />);
    expect(screen.getByText("Ny")).toBeInTheDocument();
  });

  // F4-13 (ADR 0076) — POSITIVE-ONLY: utan matchGrade-prop renderas ingen chip.
  it("does not render a match chip when matchGrade is absent (POSITIVE-ONLY)", () => {
    const { container } = render(<JobAdCard jobAd={baseAd} />);
    expect(container.querySelector(".jp-matchchip")).toBeNull();
  });

  it("renders a graded match chip when matchGrade is provided (F4-13)", () => {
    const { container } = render(
      <JobAdCard jobAd={baseAd} matchGrade="Strong" />
    );
    const chip = container.querySelector(".jp-matchchip");
    expect(chip).not.toBeNull();
    expect(chip).toHaveClass("jp-matchchip--high");
    expect(chip).toHaveTextContent("Stark match");
  });

  it("does not render a save button (FE-action-fas deferrad)", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(
      screen.queryByRole("button", { name: /spara/i })
    ).not.toBeInTheDocument();
  });
});
