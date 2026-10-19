import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobAdSourceLink } from "./job-ad-source-link";

const AF_AD = "https://arbetsformedlingen.se/platsbanken/annonser/30123456";
const EMPLOYER_PAGE = "https://career.example.se/jobs/4567890";

describe("JobAdSourceLink (#2191)", () => {
  it("names Platsbanken when the ad lives at Arbetsförmedlingen", () => {
    render(<JobAdSourceLink jobAd={{ url: AF_AD, applicationUrl: EMPLOYER_PAGE }} />);
    const link = screen.getByRole("link", { name: "Visa annonsen på Platsbanken" });
    expect(link).toHaveAttribute("href", AF_AD);
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("says the original ad when it lives anywhere else", () => {
    render(<JobAdSourceLink jobAd={{ url: "https://jobs.example.com/123", applicationUrl: EMPLOYER_PAGE }} />);
    expect(screen.getByRole("link", { name: "Visa originalannonsen" })).toHaveAttribute(
      "href",
      "https://jobs.example.com/123",
    );
  });

  it("renders nothing without an application page, where the footer's Öppna annonsen is the way to the ad", () => {
    const { container } = render(<JobAdSourceLink jobAd={{ url: AF_AD, applicationUrl: null }} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("renders nothing for an ad address that is not http(s)", () => {
    const { container } = render(
      <JobAdSourceLink jobAd={{ url: "javascript:alert(1)", applicationUrl: EMPLOYER_PAGE }} />,
    );
    expect(container).toBeEmptyDOMElement();
  });
});
