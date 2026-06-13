import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobAdDetail } from "./job-ad-detail";
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
  isNew: false,
};

describe("JobAdDetail (ADR 0053 Fas-3 fält-set)", () => {
  it("renders title, company, status pill, description and Annons-ID", () => {
    render(<JobAdDetail jobAd={baseAd} />);
    expect(
      screen.getByRole("heading", { name: "Senior Backend Developer" })
    ).toBeInTheDocument();
    expect(screen.getByText("Acme AB")).toBeInTheDocument();
    expect(screen.getByText("Aktiv")).toBeInTheDocument();
    expect(
      screen.getByText(/Vi söker en .NET-utvecklare/)
    ).toBeInTheDocument();
    expect(screen.getByText(baseAd.id)).toBeInTheDocument();
  });

  it("renders the Öppna annonsen link with safe rel attributes", () => {
    render(<JobAdDetail jobAd={baseAd} />);
    const link = screen.getByRole("link", { name: /Öppna annonsen/ });
    expect(link).toHaveAttribute("href", baseAd.url);
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("omits sista ansökningsdag when expiresAt is null", () => {
    render(<JobAdDetail jobAd={{ ...baseAd, expiresAt: null }} />);
    expect(
      screen.queryByText("Sista ansökningsdag")
    ).not.toBeInTheDocument();
  });

  it("does NOT render match, requirements, occupation or location (ADR 0053 amendment — frånvaro, ej mock)", () => {
    render(<JobAdDetail jobAd={baseAd} />);
    expect(screen.queryByText(/% match/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Krav & meriter/)).not.toBeInTheDocument();
    expect(screen.queryByText("Yrkesområde")).not.toBeInTheDocument();
  });

  it("does NOT render Spara annons or Har ansökt (FE-action-fas deferrad — ingen disabled-teater)", () => {
    render(<JobAdDetail jobAd={baseAd} />);
    expect(
      screen.queryByRole("button", { name: /spara annons/i })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /har ansökt/i })
    ).not.toBeInTheDocument();
  });

  it("omits its own header when headless (modal owns the title)", () => {
    render(<JobAdDetail jobAd={baseAd} headless />);
    expect(
      screen.queryByRole("heading", { name: "Senior Backend Developer" })
    ).not.toBeInTheDocument();
    // Status-pill renderas fortfarande (i body) i headless-läge.
    expect(screen.getByText("Aktiv")).toBeInTheDocument();
  });
});
