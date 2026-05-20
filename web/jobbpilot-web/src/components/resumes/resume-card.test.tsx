import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { ResumeCard } from "./resume-card";
import type { ResumeListItemDto } from "@/lib/types/resumes";

const baseResume: ResumeListItemDto = {
  id: "resume-1",
  name: "Backend & molnplattform",
  versionCount: 3,
  createdAt: "2026-01-15T08:00:00Z",
  updatedAt: "2026-05-13T08:00:00Z",
  isPrimary: false,
  language: "Sv",
  latestRole: "Backend-utvecklare",
  sectionCount: 4,
  topSkills: ["C#", ".NET", "Azure", "EF Core", "DDD"],
};

describe("ResumeCard (F6 P3a v3)", () => {
  it("renderar titel + roll + sektioner + språk + datum", () => {
    render(<ResumeCard resume={baseResume} />);
    expect(
      screen.getByRole("heading", { name: "Backend & molnplattform" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Backend-utvecklare")).toBeInTheDocument();
    expect(screen.getByText("4 sektioner")).toBeInTheDocument();
    expect(screen.getByText("SV")).toBeInTheDocument();
    expect(screen.getByText(/Uppd\./)).toBeInTheDocument();
  });

  it("singular 'sektion' vid sectionCount=1", () => {
    render(
      <ResumeCard resume={{ ...baseResume, sectionCount: 1 }} />,
    );
    expect(screen.getByText("1 sektion")).toBeInTheDocument();
  });

  it("renderar Standard-pill när isPrimary=true", () => {
    render(<ResumeCard resume={{ ...baseResume, isPrimary: true }} />);
    expect(screen.getByText("Standard")).toBeInTheDocument();
  });

  it("renderar INTE Standard-pill när isPrimary=false", () => {
    render(<ResumeCard resume={baseResume} />);
    expect(screen.queryByText("Standard")).not.toBeInTheDocument();
  });

  it("renderar EN-språkkod när language=En", () => {
    render(<ResumeCard resume={{ ...baseResume, language: "En" }} />);
    expect(screen.getByText("EN")).toBeInTheDocument();
    expect(screen.queryByText("SV")).not.toBeInTheDocument();
  });

  it("renderar alla topSkills som chips (max 5)", () => {
    const { container } = render(<ResumeCard resume={baseResume} />);
    const chips = container.querySelectorAll(".jp-skill-chip");
    expect(chips).toHaveLength(5);
    for (const skill of baseResume.topSkills) {
      expect(screen.getByText(skill)).toBeInTheDocument();
    }
  });

  it("renderar INGA skill-chips när topSkills är tom", () => {
    const { container } = render(
      <ResumeCard resume={{ ...baseResume, topSkills: [] }} />,
    );
    expect(container.querySelector(".jp-skill-chip")).toBeNull();
  });

  it("omitar role-rad när latestRole är null", () => {
    render(<ResumeCard resume={{ ...baseResume, latestRole: null }} />);
    expect(screen.queryByText("Backend-utvecklare")).not.toBeInTheDocument();
  });

  it("Redigera-länk pekar mot /cv/[id]", () => {
    render(<ResumeCard resume={baseResume} />);
    const link = screen.getByRole("link", { name: /Redigera/ });
    expect(link).toHaveAttribute("href", "/cv/resume-1");
  });

  it("Förhandsgranska är aria-disabled (PDF-pipeline ej byggd)", () => {
    render(<ResumeCard resume={baseResume} />);
    const btn = screen.getByRole("button", { name: /Förhandsgranska/ });
    expect(btn).toHaveAttribute("aria-disabled", "true");
  });
});
