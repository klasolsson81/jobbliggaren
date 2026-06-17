import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { CvProposedChange } from "./cv-proposed-change";
import type { ProposedChangeDto } from "@/lib/dto/parsed-resume";

/**
 * Förklarbarhets-invarianten (ADR 0074): varje förslag VISAR sin citerade evidens
 * + sin proveniens, aldrig ett naket förslag. Ersättnings-förslag visar märkt
 * före→efter (Nuvarande/Förslag — aldrig enbart färg, WCAG 1.4.1); strukturella
 * förslag visar operationen som en observations-mening utan före/efter.
 */

const replacementChange: ProposedChangeDto = {
  targetId: "exp-0-line-2",
  kind: "ClicheReplacement",
  category: "Language",
  criterionId: "A7",
  evidence: {
    kind: "TextSpan",
    start: 12,
    length: 18,
    quote: "teamplayer med driv",
    note: "klyscha utan konkret stöd",
    observation: null,
  },
  replacement: { before: "teamplayer med driv", after: "ledde ett team om fyra" },
  operation: null,
  rationale: "Konkretisera klyschan med en mätbar handling.",
  provenance: {
    kind: "KnowledgeBank",
    source: "cliche-bank",
    version: "1.2.0",
    key: "teamplayer",
    transform: null,
  },
};

const operationChange: ProposedChangeDto = {
  targetId: "header-personnummer",
  kind: "PersonnummerStrip",
  category: "AtsParsability",
  criterionId: null,
  evidence: {
    kind: "Structural",
    start: null,
    length: null,
    quote: null,
    note: null,
    observation: "Ett personnummer hittades i sidhuvudet.",
  },
  replacement: null,
  operation: { kind: "RemovePersonnummer", target: "header" },
  rationale: "Personnummer behövs inte i ett CV och bör tas bort.",
  provenance: {
    kind: "StructuralTransform",
    source: null,
    version: null,
    key: null,
    transform: "RemovePersonnummer",
  },
};

describe("CvProposedChange — ersättnings-förslag", () => {
  it("renderar märkt före/efter (Nuvarande/Förslag), aldrig enbart färg (WCAG 1.4.1)", () => {
    const { container } = render(<CvProposedChange change={replacementChange} />);

    // De textbärande etiketterna ger semantik utöver färgen (WCAG 1.4.1).
    expect(screen.getByText("Nuvarande")).toBeInTheDocument();
    expect(screen.getByText("Förslag")).toBeInTheDocument();

    // Före-värdet i .diff-before, efter-värdet i .diff-after — scope per sida så
    // att ett identiskt citat (evidence.quote === replacement.before) inte gör
    // matchningen tvetydig.
    const before = container.querySelector(".jp-improve__diff-before");
    const after = container.querySelector(".jp-improve__diff-after");
    expect(before).toHaveTextContent("teamplayer med driv");
    expect(after).toHaveTextContent("ledde ett team om fyra");
  });

  it("visar neutral pill 'Omformulering' för textbärande förslag", () => {
    render(<CvProposedChange change={replacementChange} />);
    expect(screen.getByText("Omformulering")).toBeInTheDocument();
    // Aldrig den strukturella etiketten på ett ersättnings-förslag.
    expect(screen.queryByText("Struktur")).toBeNull();
  });

  it("renderar TextSpan-evidens som citat + not", () => {
    const { container } = render(<CvProposedChange change={replacementChange} />);
    // Citatet (redan pnr-redigerat vid choke point) renderas verbatim i sitt
    // evidens-block. (Citatet är här identiskt med diffens before-sida — därav
    // scope per evidens-container.)
    const evidence = container.querySelector(".jp-improve__evidence");
    expect(evidence).not.toBeNull();
    const evidenceScope = within(evidence as HTMLElement);
    expect(evidenceScope.getByText("teamplayer med driv")).toBeInTheDocument();
    expect(evidenceScope.getByText("klyscha utan konkret stöd")).toBeInTheDocument();
  });

  it("visar KnowledgeBank-proveniens som 'Källa: {source} {version}' (key utelämnas)", () => {
    render(<CvProposedChange change={replacementChange} />);
    expect(screen.getByText("Källa: cliche-bank 1.2.0")).toBeInTheDocument();
    // key är brus för slutanvändaren och visas aldrig.
    expect(screen.queryByText(/teamplayer$/)).toBeNull();
  });

  it("visar rationale-texten", () => {
    render(<CvProposedChange change={replacementChange} />);
    expect(
      screen.getByText("Konkretisera klyschan med en mätbar handling."),
    ).toBeInTheDocument();
  });

  it("renderar INGEN strukturell operations-mening på ett ersättnings-förslag", () => {
    render(<CvProposedChange change={replacementChange} />);
    expect(screen.queryByText(/Föreslagen ändring:/)).toBeNull();
  });

  it("surfacerar INTE den råa targetId:n (opak intern apply-adress, dold i v1 — CTO Q1)", () => {
    // targetId är bokföring för det framtida godkänn-steget, inte användarnytta i
    // en display-only-vy. Den lever kvar i DTO:n men renderas aldrig — lås så att
    // en framtida återinföring failar CI (paritet med "ingen apply-knapp"-låset).
    render(<CvProposedChange change={replacementChange} />);
    expect(screen.queryByText("exp-0-line-2")).toBeNull();
  });
});

describe("CvProposedChange — operation-only förslag", () => {
  it("renderar den strukturella observations-meningen och INGET före/efter", () => {
    const { container } = render(<CvProposedChange change={operationChange} />);

    // Operations-meningen: svensk etikett ur change.kind + target. Texten är
    // uppdelad i flera text-noder i ett <p> (etikett + " på " + <code>target</code>),
    // så assertera paragrafens samlade textinnehåll i stället för en enskild nod.
    // (Etiketten "Ta bort personnummer" återkommer i provenance-foten — scope till
    // operations-paragrafen håller matchningen entydig.)
    const operation = container.querySelector(".jp-improve__operation");
    expect(operation).not.toBeNull();
    expect(operation).toHaveTextContent(
      "Föreslagen ändring: Ta bort personnummer på header",
    );
    // target renderas i en <code> inom paragrafen.
    expect(within(operation as HTMLElement).getByText("header")).toBeInTheDocument();

    // Inget märkt diff-par på ett rent strukturellt förslag.
    expect(screen.queryByText("Nuvarande")).toBeNull();
    expect(screen.queryByText("Förslag")).toBeNull();
    expect(container.querySelector(".jp-improve__diff")).toBeNull();
  });

  it("visar neutral pill 'Struktur' för rena strukturella operationer", () => {
    render(<CvProposedChange change={operationChange} />);
    expect(screen.getByText("Struktur")).toBeInTheDocument();
    expect(screen.queryByText("Omformulering")).toBeNull();
  });

  it("renderar Structural-evidens som observation", () => {
    render(<CvProposedChange change={operationChange} />);
    expect(
      screen.getByText("Ett personnummer hittades i sidhuvudet."),
    ).toBeInTheDocument();
  });

  it("visar strukturell proveniens som 'Källa: strukturell regel (...)' ur provenance.transform", () => {
    render(<CvProposedChange change={operationChange} />);
    expect(
      screen.getByText("Källa: strukturell regel (Ta bort personnummer)"),
    ).toBeInTheDocument();
  });

  it("Invariant: surfacerar aldrig ett personnummer-råvärde (bara den flaggande observationen)", () => {
    // Fixturen bär aldrig ett råvärde (ADR 0074 Invariant 1) — komponenten får
    // heller aldrig konstruera ett. Sanity: inga 10-/12-siffriga sekvenser i DOM:en.
    const { container } = render(<CvProposedChange change={operationChange} />);
    expect(container.textContent ?? "").not.toMatch(/\d{6}[-+]?\d{4}/);
  });
});
