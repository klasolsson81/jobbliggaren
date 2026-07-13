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

  // #593 (#446-uppföljning) — "tidigare ansökningar till detta företag" som LÄNK. POSITIVE-ONLY.
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

  it("renders the previous-applications line as a LINK to /foretag#ansokningshistorik when count > 0", () => {
    render(<JobAdDetail jobAd={baseAd} previousApplicationCount={3} />);
    // Plural sentence + a valid link (the detail view has no outer <a>, unlike the list card). The
    // count is a plain integer — org.nr is never passed to this component (§5, enskild firma =
    // personnummer), so the affordance structurally cannot surface one.
    expect(
      screen.getByText(
        "Du har minst 3 tidigare ansökningar till detta företag. Sammanställningen kan vara ofullständig."
      )
    ).toBeInTheDocument();
    const link = screen.getByRole("link", { name: "Visa ansökningshistorik" });
    expect(link).toHaveAttribute("href", "/foretag#ansokningshistorik");
  });

  it("renders the singular previous-applications sentence for count 1", () => {
    render(<JobAdDetail jobAd={baseAd} previousApplicationCount={1} />);
    expect(
      screen.getByText(
        "Du har minst 1 tidigare ansökan till detta företag. Sammanställningen kan vara ofullständig."
      )
    ).toBeInTheDocument();
  });

  // #824 PR 4 — the detail view has room the card does not, so it carries BOTH halves of the hedge: the
  // floor marker on the number and the incompleteness of the compilation the link leads to. Losing
  // either half turns the sentence back into an unreserved factual claim about the user's own data
  // (Art. 5(1)(a)/(d)).
  it("presents the count as a floor AND discloses the incompleteness (#824)", () => {
    render(<JobAdDetail jobAd={baseAd} previousApplicationCount={3} />);
    // ANCHORED REGEX, deliberately — an exact-string guard here CANNOT FAIL (code-reviewer M1). The
    // sentence and the disclosure share one <p>, so getNodeText() returns both; dropping "minst" would
    // yield "Du har 3 … företag. Sammanställningen …", which never equals the bare-total matcher — the
    // guard would return null and pass while the surface shows a total. A test that cannot fail for its
    // stated reason IS the #843 defect this PR family exists to condemn. `^` pins the mutation itself.
    expect(
      screen.queryByText(/^Du har 3 tidigare ansökningar/)
    ).toBeNull();
    expect(
      screen.getByText(/Sammanställningen kan vara ofullständig/)
    ).toBeInTheDocument();
  });
});
