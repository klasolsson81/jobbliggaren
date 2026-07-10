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
  openFindingCount: null,
  origin: "Import",
  template: "Klar",
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

  it("renderar Förhandsgranska-knapp bredvid Redigera (render-by-Resume-id levererad, TD-112)", () => {
    render(<ResumeCard resume={baseResume} />);
    // Preview-triggern (CvPreview) finns nu på det befordrade kortet — render-by-Resume-id-
    // vägen (/api/cv/{id}/preview) ersatte den tidigare borttagna stuben (#202).
    expect(
      screen.getByRole("button", { name: /Förhandsgranska/ }),
    ).toBeInTheDocument();
    // Redigera-länken finns kvar bredvid preview-knappen i actions-raden.
    expect(
      screen.getByRole("link", { name: /Redigera/ }),
    ).toBeInTheDocument();
  });
});

// Fas 4b PR-8 (CTO-bind Q1) — granskningsstatus-badge ur den DEK-fria finding-
// ledgern. §5-ärlighet: "0" får ALDRIG betyda "inte granskad".
describe("ResumeCard — granskningsstatus-badge (§5-ärlighet)", () => {
  it("openFindingCount null → 'Granska', och VARKEN 'Inga åtgärder' ELLER '0 att åtgärda' (honesty pin)", () => {
    render(<ResumeCard resume={{ ...baseResume, openFindingCount: null }} />);
    expect(screen.getByText("Granska")).toBeInTheDocument();
    // Det centrala §5-pinnet: en icke-granskad status renderar aldrig en noll-signal.
    expect(screen.queryByText("Inga åtgärder")).not.toBeInTheDocument();
    expect(screen.queryByText("0 att åtgärda")).not.toBeInTheDocument();
    expect(screen.queryByText(/att åtgärda/)).not.toBeInTheDocument();
  });

  it("openFindingCount 0 → 'Inga åtgärder' (granskad-och-ren), inte 'Granska'", () => {
    render(<ResumeCard resume={{ ...baseResume, openFindingCount: 0 }} />);
    expect(screen.getByText("Inga åtgärder")).toBeInTheDocument();
    expect(screen.queryByText("Granska")).not.toBeInTheDocument();
    expect(screen.queryByText(/att åtgärda/)).not.toBeInTheDocument();
  });

  it("openFindingCount 3 → '3 att åtgärda', inte 'Granska'/'Inga åtgärder'", () => {
    render(<ResumeCard resume={{ ...baseResume, openFindingCount: 3 }} />);
    expect(screen.getByText("3 att åtgärda")).toBeInTheDocument();
    expect(screen.queryByText("Granska")).not.toBeInTheDocument();
    expect(screen.queryByText("Inga åtgärder")).not.toBeInTheDocument();
  });
});

// Ursprungs-badge (ADR 0096): Import → "Importerad", Template → "Skapad" + mallnamn,
// Legacy → ingen badge.
describe("ResumeCard — ursprungs-badge", () => {
  it("origin Import → 'Importerad'-badge, ingen 'Skapad'", () => {
    render(<ResumeCard resume={{ ...baseResume, origin: "Import" }} />);
    expect(screen.getByText("Importerad")).toBeInTheDocument();
    expect(screen.queryByText("Skapad")).not.toBeInTheDocument();
    // Mall-metaraden visas bara för Template-ursprung.
    expect(screen.queryByText(/^Mall:/)).not.toBeInTheDocument();
  });

  it("origin Template → 'Skapad'-badge + 'Mall: Klar' (templateName-map)", () => {
    render(
      <ResumeCard
        resume={{ ...baseResume, origin: "Template", template: "Klar" }}
      />,
    );
    expect(screen.getByText("Skapad")).toBeInTheDocument();
    expect(screen.getByText("Mall: Klar")).toBeInTheDocument();
    expect(screen.queryByText("Importerad")).not.toBeInTheDocument();
  });

  it("origin Template med MorkPanel → 'Mall: Mörk panel' (templateName-map)", () => {
    render(
      <ResumeCard
        resume={{ ...baseResume, origin: "Template", template: "MorkPanel" }}
      />,
    );
    expect(screen.getByText("Mall: Mörk panel")).toBeInTheDocument();
  });

  it("origin Legacy → ingen ursprungs-badge (varken 'Importerad' eller 'Skapad')", () => {
    render(<ResumeCard resume={{ ...baseResume, origin: "Legacy" }} />);
    expect(screen.queryByText("Importerad")).not.toBeInTheDocument();
    expect(screen.queryByText("Skapad")).not.toBeInTheDocument();
    expect(screen.queryByText(/^Mall:/)).not.toBeInTheDocument();
  });
});
