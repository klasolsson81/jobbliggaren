import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { CvImprovePanel } from "./cv-improve-panel";
import type {
  CvImprovementDto,
  ProposedChangeDto,
  RubricCategory,
} from "@/lib/dto/parsed-resume";

/**
 * Panelen surfacerar de deterministiska förbättringsförslagen display-only: ett
 * kort per rubrik-kategori med per-kategori-räkning ("3 förslag" — aldrig ett
 * 0–100-betyg, Goodhart). Ingen tillämpa/godkänn/avvisa-knapp, ingen kryssruta
 * (CLAUDE.md §5). `improvements === null` degraderar civilt (role="status").
 */

const PARSED_ID = "11111111-1111-4111-8111-111111111111";

function makeChange(
  category: RubricCategory,
  targetId: string,
  overrides: Partial<ProposedChangeDto> = {},
): ProposedChangeDto {
  return {
    targetId,
    kind: "ClicheReplacement",
    category,
    criterionId: "A7",
    evidence: {
      kind: "TextSpan",
      start: 0,
      length: 4,
      quote: "lagspelare",
      note: null,
      observation: null,
    },
    replacement: { before: "lagspelare", after: "ledde teamet" },
    operation: null,
    rationale: "Konkretisera.",
    provenance: {
      kind: "KnowledgeBank",
      source: "cliche-bank",
      version: "1.2.0",
      key: "lagspelare",
      transform: null,
    },
    ...overrides,
  };
}

function makeImprovements(
  changes: ProposedChangeDto[],
): CvImprovementDto {
  return {
    clicheListVersion: "cliche-1.2.0",
    verbMappingVersion: "verb-1.1.0",
    rubricVersion: "rubrik-1.0.0",
    profile: "Ats",
    changes,
  };
}

describe("CvImprovePanel — grupperade förslag", () => {
  const improvements = makeImprovements([
    makeChange("Language", "lang-1", { rationale: "Språkförslag ett." }),
    makeChange("Language", "lang-2", { rationale: "Språkförslag två." }),
    makeChange("Content", "content-1", { rationale: "Innehållsförslag ett." }),
  ]);

  it("grupperar förslagen i ett kort per kategori (svenska etiketter)", () => {
    render(
      <CvImprovePanel
        improvements={improvements}
        parsedId={PARSED_ID}
        profile="Ats"
      />,
    );

    expect(
      screen.getByRole("heading", { name: "Språk", level: 3 }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "Innehåll", level: 3 }),
    ).toBeInTheDocument();
  });

  it("visar per-kategori-räkning som skanninfo, aldrig ett betyg (singular/plural)", () => {
    render(
      <CvImprovePanel
        improvements={improvements}
        parsedId={PARSED_ID}
        profile="Ats"
      />,
    );

    // Språk har två förslag, Innehåll ett.
    expect(screen.getByText("2 förslag")).toBeInTheDocument();
    expect(screen.getByText("1 förslag")).toBeInTheDocument();
  });

  it("renderar varje förslag inom sitt kategori-kort (Språk-kortet bär sina två förslag)", () => {
    render(
      <CvImprovePanel
        improvements={improvements}
        parsedId={PARSED_ID}
        profile="Ats"
      />,
    );

    const languageHeading = screen.getByRole("heading", {
      name: "Språk",
      level: 3,
    });
    // Klättra till kortet (Card är heading-ens närmaste gemensamma container).
    // Förankra mot renderad rationale — targetId surfacas inte längre (CTO Q1),
    // så det användarsynliga rationale-fältet är den stabila ankaren per förslag.
    const card = languageHeading.closest("[data-slot='card']");
    expect(card).not.toBeNull();
    const cardScope = within(card as HTMLElement);
    expect(cardScope.getByText("Språkförslag ett.")).toBeInTheDocument();
    expect(cardScope.getByText("Språkförslag två.")).toBeInTheDocument();
    expect(cardScope.queryByText("Innehållsförslag ett.")).toBeNull();
  });

  it("renderar versions-foten (rubrik/klyschor/verb) — determinismens proveniens", () => {
    render(
      <CvImprovePanel
        improvements={improvements}
        parsedId={PARSED_ID}
        profile="Ats"
      />,
    );

    expect(screen.getByText("Rubrik rubrik-1.0.0")).toBeInTheDocument();
    expect(screen.getByText("Klyschor cliche-1.2.0")).toBeInTheDocument();
    expect(screen.getByText("Verb verb-1.1.0")).toBeInTheDocument();
  });

  it("renderar profil-växeln (förbättra-vyns basePath)", () => {
    render(
      <CvImprovePanel
        improvements={improvements}
        parsedId={PARSED_ID}
        profile="Ats"
      />,
    );

    const atsLink = screen.getByRole("link", { name: "ATS-profil" });
    expect(atsLink).toHaveAttribute(
      "href",
      `/cv/granska/${PARSED_ID}/forbattra?profile=Ats`,
    );
    expect(atsLink).toHaveAttribute("aria-current", "true");
    expect(screen.getByRole("link", { name: "Visuell profil" })).toHaveAttribute(
      "href",
      `/cv/granska/${PARSED_ID}/forbattra?profile=Visual`,
    );
  });
});

describe("CvImprovePanel — invarianter (ingen score, ingen tillämpa-interaktion)", () => {
  const improvements = makeImprovements([makeChange("Language", "lang-1")]);

  it("renderar ALDRIG ett 0–100-betyg eller en opak totalsumma", () => {
    const { container } = render(
      <CvImprovePanel
        improvements={improvements}
        parsedId={PARSED_ID}
        profile="Ats"
      />,
    );

    const text = container.textContent ?? "";
    // Inga ord som antyder ett poäng/betyg.
    expect(text).not.toMatch(/poäng|betyg|score|\/\s*100|av\s*100/i);
  });

  it("renderar INGEN tillämpa/godkänn/avvisa-knapp och ingen kryssruta (§5 — regelmotor skriver aldrig om tyst)", () => {
    render(
      <CvImprovePanel
        improvements={improvements}
        parsedId={PARSED_ID}
        profile="Ats"
      />,
    );

    // Inga interaktiva mutations-kontroller alls.
    expect(screen.queryByRole("button")).toBeNull();
    expect(screen.queryByRole("checkbox")).toBeNull();
    for (const name of [/tillämpa/i, /godkänn/i, /avvisa/i, /acceptera/i]) {
      expect(screen.queryByText(name)).toBeNull();
    }
  });
});

describe("CvImprovePanel — tom-tillstånd", () => {
  it("visar tom-notis när changes-arrayen är tom (granskningen bär bedömningen)", () => {
    render(
      <CvImprovePanel
        improvements={makeImprovements([])}
        parsedId={PARSED_ID}
        profile="Ats"
      />,
    );

    expect(
      screen.getByText(
        "Inga förbättringsförslag för den här profilen. Granskningen visar vad som redan bedömts.",
      ),
    ).toBeInTheDocument();
    // Versions-foten står kvar (proveniens visas även när tomt).
    expect(screen.getByText("Rubrik rubrik-1.0.0")).toBeInTheDocument();
    // Inga kategori-kort.
    expect(screen.queryByRole("heading", { level: 3 })).toBeNull();
  });
});

describe("CvImprovePanel — civil degradering (improvements === null)", () => {
  it("visar en lugn notis med role='status' (sidan 404:ar aldrig på ett förbättrings-fel)", () => {
    render(
      <CvImprovePanel improvements={null} parsedId={PARSED_ID} profile="Ats" />,
    );

    const status = screen.getByRole("status");
    expect(status).toHaveTextContent(/Förslagen kunde inte laddas just nu/);
    expect(status).toHaveTextContent(
      /Tolkningen av ditt CV och granskningen påverkas inte/,
    );
  });

  it("behåller rubrik + profil-växel i degraderat läge (sid-skalet står kvar)", () => {
    render(
      <CvImprovePanel improvements={null} parsedId={PARSED_ID} profile="Visual" />,
    );

    expect(
      screen.getByRole("heading", { name: "Förbättringsförslag" }),
    ).toBeInTheDocument();
    const visualLink = screen.getByRole("link", { name: "Visuell profil" });
    expect(visualLink).toHaveAttribute("aria-current", "true");
    expect(visualLink).toHaveAttribute(
      "href",
      `/cv/granska/${PARSED_ID}/forbattra?profile=Visual`,
    );
  });

  it("renderar ingen versions-fot i degraderat läge (ingen DTO att läsa proveniens ur)", () => {
    render(
      <CvImprovePanel improvements={null} parsedId={PARSED_ID} profile="Ats" />,
    );
    expect(screen.queryByText(/^Rubrik /)).toBeNull();
  });
});
