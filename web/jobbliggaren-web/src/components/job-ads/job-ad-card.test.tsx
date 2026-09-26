import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobAdCard } from "./job-ad-card";
import type { JobAdDto } from "@/lib/dto/job-ads";

// publishedAt > 7 dygn sedan — historiskt för att undvika färskhets-taggen (nu
// BORTTAGEN, #1000-review); värdet lämnas oförändrat så heading-assertions håller.
const baseAd: JobAdDto = {
  id: "11111111-1111-1111-1111-111111111111",
  title: "Senior Backend Developer",
  companyName: "Acme AB",
  url: "https://example.com/jobb/123",
  source: "Platsbanken",
  status: "Active",
  publishedAt: "2026-04-01T08:00:00Z",
  expiresAt: "2026-06-13T08:00:00Z",
  createdAt: "2026-04-01T08:01:00Z",
};

/** The IDREFs the title link's aria-describedby names, resolved in order. */
function describedRows(link: HTMLElement): Array<HTMLElement | null> {
  return (link.getAttribute("aria-describedby") ?? "")
    .split(" ")
    .filter(Boolean)
    .map((id) => document.getElementById(id));
}

describe("JobAdCard (v3 .jp-job-rad)", () => {
  it("renders title and company", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(
      screen.getByRole("heading", { name: "Senior Backend Developer" })
    ).toBeInTheDocument();
    expect(screen.getByText("Acme AB")).toBeInTheDocument();
  });

  // #1828 (design-reviewer B1, the row family's house form): the title is the card's only
  // link and is named by its own text — no aria-label replaces the card's content.
  it("the title is the card's only link, to /jobb/[id], named by the title alone", () => {
    render(<JobAdCard jobAd={baseAd} />);
    const link = screen.getByRole("link", { name: "Senior Backend Developer" });
    expect(link).toHaveAttribute("href", `/jobb/${baseAd.id}`);
    expect(link).toHaveClass("jp-job__rowlink");
    expect(link).not.toHaveAttribute("aria-label");
    expect(link).not.toHaveAttribute("aria-labelledby");
    expect(screen.getAllByRole("link")).toHaveLength(1);
  });

  it("the link's description names every rendered row in visual order, and no IDREF dangles", () => {
    const { container } = render(
      <JobAdCard
        jobAd={baseAd}
        isNew={true}
        isFollowed={true}
        isSaved={true}
        isApplied={true}
        matchGrade="Strong"
        previousApplicationCount={3}
      />
    );
    const link = screen.getByRole("link", { name: "Senior Backend Developer" });
    const rows = describedRows(link);
    expect(rows).not.toContain(null);
    expect(rows).toEqual([
      container.querySelector(".jp-job-tags"),
      container.querySelector(".jp-matchchip"),
      container.querySelector(".jp-job__company"),
      ...Array.from(container.querySelectorAll(".jp-job__meta")),
    ]);
    // Security-auditor's condition (d): the counter line is ONE described element that carries
    // the whole signed text, and "Minst" is its own word in the computed description.
    expect(link).toHaveAccessibleDescription(
      expect.stringMatching(/(^|\s)Minst 3 tidigare ansökningar till företaget(\s|$)/)
    );
  });

  // Chrome drops a whitespace-only text node between label and value from the computed
  // description ("Publiceradidag", CDP-measured #1828); jsdom does not, so this pins the form
  // that avoids it: the label's own text node carries the space.
  it("each meta label carries its trailing space in its own text node", () => {
    const { container } = render(<JobAdCard jobAd={baseAd} />);
    const labels = Array.from(container.querySelectorAll(".jp-job__meta > span")).map(
      (span) => span.firstChild?.textContent
    );
    expect(labels).toEqual(["Publicerad ", "Sista ansökningsdag "]);
  });

  it("a bare card describes only the company and the meta line", () => {
    const { container } = render(<JobAdCard jobAd={baseAd} />);
    const link = screen.getByRole("link", { name: "Senior Backend Developer" });
    expect(describedRows(link)).toEqual([
      container.querySelector(".jp-job__company"),
      container.querySelector(".jp-job__meta"),
    ]);
  });

  // #1000 (V1) — BEVAKAR = du bevakar arbetsgivaren: `isFollowed` driver BÅDE
  // kortets `data-followed`-vänsterkant OCH BEVAKAR-taggen.
  it("#1000 — sätter data-followed + renderar BEVAKAR-tagg när isFollowed=true", () => {
    const { container } = render(<JobAdCard jobAd={baseAd} isFollowed={true} />);
    expect(container.querySelector("article.jp-job")).toHaveAttribute("data-followed", "");
    expect(screen.getByText("Bevakar")).toBeInTheDocument();
  });

  it("#1000 — inget data-followed + ingen BEVAKAR när isFollowed=false (default)", () => {
    const { container } = render(<JobAdCard jobAd={baseAd} />);
    expect(container.querySelector("article.jp-job")).not.toHaveAttribute("data-followed");
    expect(screen.queryByText("Bevakar")).not.toBeInTheDocument();
  });

  // #380 — radlänken bär list-URL:ens view-state (filter + match + sort + sök)
  // så soft-nav till modalen inte tappar filter/match-läget (children-slotten
  // re-rendras annars till tomma searchParams under modalen; router.back()
  // återställer bara modal-slotten). `listQuery` byggs i `JobbResults` via
  // `buildJobbHref` (+ page). Default tom = naken länk.
  it("#380 — bär list-staten (relaterade + grader + sortering + sök) i radlänken", () => {
    const listQuery =
      "q=backend&occupationGroup=MVqp_eS8_kDZ&matchGrades=Strong&relaterade=on&sortBy=Relevance";
    render(<JobAdCard jobAd={baseAd} listQuery={listQuery} />);
    const link = screen.getByRole("link", { name: "Senior Backend Developer" });
    // Modal-URL:en speglar listans URL exakt → router.back() bevarar HELA
    // filter-/match-läget. relaterade=on tas dessutom in i modalens grad-anrop.
    expect(link).toHaveAttribute("href", `/jobb/${baseAd.id}?${listQuery}`);
  });

  it("#380 — tom listQuery (gäst-/övrig yta) ger en naken länk utan query", () => {
    render(<JobAdCard jobAd={baseAd} listQuery="" />);
    const link = screen.getByRole("link", { name: "Senior Backend Developer" });
    expect(link).toHaveAttribute("href", `/jobb/${baseAd.id}`);
  });

  // #1828 — Platsbanken is the one source /jobb ingests and the hero above the list names it,
  // so the card does not repeat it; another source is information and keeps its label.
  it("does not print Platsbanken, the one source /jobb ingests", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(screen.queryByText("Platsbanken")).not.toBeInTheDocument();
    expect(screen.getByText(/Publicerad/)).toBeInTheDocument();
  });

  it("prints any other source", () => {
    render(<JobAdCard jobAd={{ ...baseAd, source: "Eures" }} />);
    expect(screen.getByText("EURES")).toBeInTheDocument();
  });

  it("omits sista ansökningsdag when expiresAt is null", () => {
    render(<JobAdCard jobAd={{ ...baseAd, expiresAt: null }} />);
    expect(screen.queryByText(/Sista ansökningsdag/)).not.toBeInTheDocument();
  });

  it("renders sista ansökningsdag when expiresAt is set", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(screen.getByText(/Sista ansökningsdag/)).toBeInTheDocument();
  });

  // NY = oläst (#293/#306): driven av `isNew`-propen (beräknad i JobbResults
  // mot oläst-watermarken), INTE av ett borttaget JobAdDto.isNew-fält.
  it("does not render the Ny flag without the isNew prop (default false)", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(screen.queryByText("Ny")).not.toBeInTheDocument();
  });

  it("renders the Ny flag when isNew prop is true", () => {
    render(<JobAdCard jobAd={baseAd} isNew={true} />);
    expect(screen.getByText("Ny")).toBeInTheDocument();
  });

  // F4-13 (ADR 0076) — POSITIVE-ONLY: utan matchGrade-prop renderas ingen chip.
  it("does not render a match chip when matchGrade is absent (POSITIVE-ONLY)", () => {
    const { container } = render(<JobAdCard jobAd={baseAd} />);
    expect(container.querySelector(".jp-matchchip")).toBeNull();
  });

  it("renders a graded match chip when matchGrade is provided (F4-13)", () => {
    const { container } = render(
      <JobAdCard jobAd={baseAd} matchGrade="Strong" />
    );
    const chip = container.querySelector(".jp-matchchip");
    expect(chip).not.toBeNull();
    expect(chip).toHaveClass("jp-matchchip--high");
    expect(chip).toHaveTextContent("Stark match");
  });

  it("does not render a save button (FE-action-fas deferrad)", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(
      screen.queryByRole("button", { name: /spara/i })
    ).not.toBeInTheDocument();
  });

  // #446 (#311) — räknaren. POSITIVE-ONLY: bara när räknaren > 0.
  it("does not render the previous-applications line without the prop (POSITIVE-ONLY)", () => {
    render(<JobAdCard jobAd={baseAd} />);
    expect(
      screen.queryByText(/tidigare ansökning/i)
    ).not.toBeInTheDocument();
  });

  it("does not render the previous-applications line when the count is 0", () => {
    render(<JobAdCard jobAd={baseAd} previousApplicationCount={0} />);
    expect(
      screen.queryByText(/tidigare ansökning/i)
    ).not.toBeInTheDocument();
  });

  it("renders the singular previous-applications line for count 1 (no org.nr)", () => {
    render(<JobAdCard jobAd={baseAd} previousApplicationCount={1} />);
    // ICU one-branch: "ansökan", not "ansökningar"; a plain integer, never an org.nr.
    expect(
      screen.getByText("Minst 1 tidigare ansökan till företaget")
    ).toBeInTheDocument();
  });

  it("renders the plural previous-applications line for count > 1", () => {
    render(<JobAdCard jobAd={baseAd} previousApplicationCount={3} />);
    expect(
      screen.getByText("Minst 3 tidigare ansökningar till företaget")
    ).toBeInTheDocument();
  });

  // #824 PR 4 — the count is a FLOOR, not a total (ADR 0144 D4 row 9): "Minst" governs the number.
  it("presents the count as a floor — never as a total (#824)", () => {
    render(<JobAdCard jobAd={baseAd} previousApplicationCount={3} />);
    // Anchored on the signed form's own start: with "Minst" removed from the catalogue value the
    // line reads "3 tidigare ansökningar …" and this matcher finds it.
    expect(
      screen.queryByText(/^3 tidigare ansökningar/)
    ).toBeNull();
  });

  // The counter is informative text, never a link (B1): the title is the card's only link.
  it("renders the previous-applications line as plain text, not a second link", () => {
    render(<JobAdCard jobAd={baseAd} previousApplicationCount={2} />);
    expect(screen.getAllByRole("link")).toHaveLength(1);
  });
});
