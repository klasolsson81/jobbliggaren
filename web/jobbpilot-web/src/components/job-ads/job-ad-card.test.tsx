import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobAdCard } from "./job-ad-card";
import type { JobAdDto } from "@/lib/dto/job-ads";

const baseAd: JobAdDto = {
  id: "11111111-1111-1111-1111-111111111111",
  title: "Senior Backend Developer",
  companyName: "Acme AB",
  description: "Vi söker en .NET-utvecklare för långsiktigt uppdrag.",
  url: "https://example.com/jobb/123",
  source: "Platsbanken",
  status: "Active",
  publishedAt: "2026-05-13T08:00:00Z",
  expiresAt: "2026-06-13T08:00:00Z",
  createdAt: "2026-05-13T08:01:00Z",
};

describe("JobAdCard", () => {
  it("renders title, company and status badge", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(
      screen.getByRole("heading", { name: "Senior Backend Developer" })
    ).toBeInTheDocument();
    expect(screen.getByText("Acme AB")).toBeInTheDocument();
    expect(screen.getByText("Aktiv")).toBeInTheDocument();
  });

  it("renders external link with safe rel attributes", () => {
    render(<JobAdCard jobAd={baseAd} />);
    const link = screen.getByRole("link", { name: "Läs hela annonsen" });
    expect(link).toHaveAttribute("href", baseAd.url);
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("omits expiresAt row when null", () => {
    render(<JobAdCard jobAd={{ ...baseAd, expiresAt: null }} />);
    expect(screen.queryByText("Sista ansökningsdag:")).not.toBeInTheDocument();
  });

  it("truncates long descriptions with ellipsis", () => {
    const longDesc = "Lorem ipsum dolor sit amet ".repeat(50);
    render(<JobAdCard jobAd={{ ...baseAd, description: longDesc }} />);
    const desc = screen.getByText(/Lorem ipsum/);
    expect(desc.textContent?.endsWith("…")).toBe(true);
  });

  it("renders source label in Swedish", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(screen.getByText("Källa:")).toBeInTheDocument();
    expect(screen.getByText("Platsbanken")).toBeInTheDocument();
  });
});
