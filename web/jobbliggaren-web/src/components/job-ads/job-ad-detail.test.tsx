import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobAdDetail } from "./job-ad-detail";
import type { AdContactDto, JobAdDetailDto } from "@/lib/dto/job-ads";

// #745 — the LIST type `JobAdDto` no longer carries `description`; the detail component
// takes the detail projection minus its contacts block. This fixture mirrors that prop.
const baseAd: Omit<JobAdDetailDto, "contacts"> = {
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

const RECRUITER_NOTICE = "Är du kontaktperson i annonsen? Läs hur vi behandlar kontaktuppgifter.";
const declaredContact: AdContactDto = {
  name: "Anna Lindqvist",
  role: "Rekryterande chef",
  email: "anna@example.com",
  phone: null,
  isDerived: false,
};

describe("JobAdDetail", () => {
  it("renders title, company and description; an active ad carries no pill and no internal id (#1828)", () => {
    render(<JobAdDetail jobAd={baseAd} />);
    expect(
      screen.getByRole("heading", { name: "Senior Backend Developer" })
    ).toBeInTheDocument();
    expect(screen.getByText("Acme AB")).toBeInTheDocument();
    expect(
      screen.getByText(/Vi söker en .NET-utvecklare/)
    ).toBeInTheDocument();
    expect(screen.queryByText("Aktiv")).not.toBeInTheDocument();
    expect(screen.queryByText(baseAd.id)).not.toBeInTheDocument();
  });

  it("an archived ad leads its meta line with the pill 'Arkiverad'", () => {
    const { container } = render(
      <JobAdDetail jobAd={{ ...baseAd, status: "Archived" }} headless />
    );
    const meta = container.querySelector(".jp-modal__body > .jp-job__meta");
    expect(meta?.firstElementChild).toHaveTextContent("Arkiverad");
    expect(meta?.firstElementChild).toHaveClass("jp-pill", "jp-pill--neutral");
  });

  it("renders the dates in the card's meta form", () => {
    const { container } = render(<JobAdDetail jobAd={baseAd} />);
    const meta = container.querySelector(".jp-modal__body > .jp-job__meta");
    expect(meta).toHaveTextContent(/Publicerad/);
    expect(meta).toHaveTextContent(/Sista ansökningsdag/);
    expect(container.querySelector(".jp-modal__metarow")).toBeNull();
  });

  it("the ad text is a region named by its heading (#1828 B3)", () => {
    render(<JobAdDetail jobAd={baseAd} headless />);
    const region = screen.getByRole("region", { name: "Annonsbeskrivning" });
    expect(region).toHaveTextContent(/Vi söker en .NET-utvecklare/);
  });

  // #1000 (V1) — modalen bär INGEN separat BEVAKAR-tagg (skulle bli en load-time-
  // snapshot mot live-toggeln, design-reviewer 2026-07-20). Regressionsvakt: ett
  // följt läge visar follow-STATE via togglens label, inte en header-tagg.
  it("#1000 — ingen BEVAKAR-tagg i modal-headern (state bärs av follow-toggeln, ej en stale-snapshot)", () => {
    render(
      <JobAdDetail
        jobAd={baseAd}
        followState={{ companyWatchId: "cw-1", followable: true }}
      />,
    );
    expect(screen.queryByText("Bevakar")).not.toBeInTheDocument();
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
      screen.queryByText(/Sista ansökningsdag/)
    ).not.toBeInTheDocument();
  });

  it("does NOT render a match section without match data (frånvaro, ej mock)", () => {
    render(<JobAdDetail jobAd={baseAd} />);
    expect(screen.queryByText(/% match/)).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Matchning" })).not.toBeInTheDocument();
  });

  it("does NOT render Spara annons or Har ansökt without user actions (ingen disabled-teater)", () => {
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
  });

  // #593 (#446-uppföljning) — räknaren + länk till historiken. POSITIVE-ONLY.
  it("does NOT render the previous-applications line without the prop (POSITIVE-ONLY)", () => {
    render(<JobAdDetail jobAd={baseAd} />);
    expect(screen.queryByText(/tidigare ansökning/i)).not.toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: "Visa ansökningshistorik" })
    ).not.toBeInTheDocument();
  });

  it("does NOT render the previous-applications line when the count is 0", () => {
    render(<JobAdDetail jobAd={baseAd} previousApplicationCount={0} />);
    expect(screen.queryByText(/tidigare ansökning/i)).not.toBeInTheDocument();
  });

  it("renders the previous-applications line with a link to /foretag/historik when count > 0", () => {
    render(<JobAdDetail jobAd={baseAd} previousApplicationCount={3} />);
    // A plain integer — org.nr is never passed to this component (§5, enskild firma =
    // personnummer), so the line structurally cannot surface one.
    expect(
      screen.getByText("Minst 3 tidigare ansökningar till företaget.")
    ).toBeInTheDocument();
    const link = screen.getByRole("link", { name: "Visa ansökningshistorik" });
    expect(link).toHaveAttribute("href", "/foretag/historik");
  });

  it("renders the singular previous-applications line for count 1", () => {
    render(<JobAdDetail jobAd={baseAd} previousApplicationCount={1} />);
    expect(
      screen.getByText("Minst 1 tidigare ansökan till företaget.")
    ).toBeInTheDocument();
  });

  // #824 PR 4 — the count is a FLOOR (ADR 0144 D4 row 9): "Minst" governs the number.
  it("presents the count as a floor — never as a total (#824)", () => {
    render(<JobAdDetail jobAd={baseAd} previousApplicationCount={3} />);
    // Anchored on the signed form's own start: with "Minst" removed from the catalogue value the
    // line reads "3 tidigare ansökningar …" and this matcher finds it.
    expect(
      screen.queryByText(/^3 tidigare ansökningar/)
    ).toBeNull();
  });

  // ADR 0144 D4 row 10 (Art. 14(5)(b), #842 R5): the recruiter notice renders on the detail
  // surface with or without contacts, visible, pointing at the public notice (security-auditor
  // #1828 Minor 1). The contact block renders nothing for [], so the link is its sibling.
  it.each([
    ["without contacts", [] as AdContactDto[]],
    ["with contacts", [declaredContact]],
  ])("renders the recruiter notice %s", (_label, contacts) => {
    render(<JobAdDetail jobAd={baseAd} contacts={contacts} headless />);
    const link = screen.getByRole("link", { name: RECRUITER_NOTICE });
    expect(link).toHaveAttribute("href", "/kontaktperson-i-annons");
  });

  it("places the recruiter notice directly after the contact block", () => {
    render(<JobAdDetail jobAd={baseAd} contacts={[declaredContact]} headless />);
    const block = screen.getByRole("region", { name: "Kontakt" });
    const notice = screen.getByRole("link", { name: RECRUITER_NOTICE }).closest("p");
    expect(block.nextElementSibling).toBe(notice);
  });
});
