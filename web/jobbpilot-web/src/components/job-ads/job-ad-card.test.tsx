import { describe, it, expect, beforeEach } from "vitest";
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
  isNew: false,
};

beforeEach(() => {
  window.localStorage.clear();
});

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

  it("does not render the Ny flag when isNew is false (ADR 0042 Beslut E)", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(screen.queryByText("Ny")).not.toBeInTheDocument();
  });

  it("renders the Ny flag when isNew is true", () => {
    render(<JobAdCard jobAd={{ ...baseAd, isNew: true }} />);
    expect(screen.getByText("Ny")).toBeInTheDocument();
  });

  it("does not render a match chip (no match domain — ADR 0053 amendment)", () => {
    const { container } = render(<JobAdCard jobAd={baseAd} />);
    expect(container.querySelector(".jp-matchchip")).toBeNull();
  });

  it("does not render a save button (FE-action-fas deferrad)", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(
      screen.queryByRole("button", { name: /spara/i })
    ).not.toBeInTheDocument();
  });
});
