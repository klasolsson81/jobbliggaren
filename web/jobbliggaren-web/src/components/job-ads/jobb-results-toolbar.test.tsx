import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobbResultsToolbar } from "./jobb-results-toolbar";

const pushMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

const resolvedLabels: Record<string, string> = {
  CifL_Rzy_Mku: "Stockholms län",
  MVqp_eS8_kDZ: "Systemutvecklare",
  zHxw_uJZ_NNh: "Solna",
};

beforeEach(() => {
  pushMock.mockClear();
});

describe("JobbResultsToolbar — träffar + chips + sort", () => {
  it("visar mono-formaterat antal träffar", () => {
    render(
      <JobbResultsToolbar
        totalCount={1234}
        occupationGroup={[]}
        region={[]}
        municipality={[]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={{}}
        q=""
        sortBy="PublishedAtDesc"
        hasStatedDesiredOccupation
      />,
    );
    // sv-SE grupperar med non-breaking space.
    expect(screen.getByRole("status")).toHaveTextContent(/1\s234 träffar/);
  });

  it("renderar aktiva chips via resolverad label och tar bort vid ×", async () => {
    const user = userEvent.setup();
    render(
      <JobbResultsToolbar
        totalCount={3}
        occupationGroup={["MVqp_eS8_kDZ"]}
        region={["CifL_Rzy_Mku"]}
        municipality={[]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={resolvedLabels}
        q="backend"
        sortBy="PublishedAtDesc"
        hasStatedDesiredOccupation
      />,
    );
    expect(screen.getByText("Stockholms län")).toBeInTheDocument();
    expect(screen.getByText("Systemutvecklare")).toBeInTheDocument();

    await user.click(
      screen.getByRole("button", { name: "Ta bort filter Stockholms län" }),
    );
    // region bort, occupationGroup + q bevarade. E2j: toolbar-handling =
    // avsiktlig sökning → commit-intent (?commit=true, Klas-val 2026-06-12).
    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=MVqp_eS8_kDZ&q=backend&commit=true",
    );
  });

  it("kommun-chip renderas och × tar bort rätt axel (E2b)", async () => {
    const user = userEvent.setup();
    render(
      <JobbResultsToolbar
        totalCount={2}
        occupationGroup={[]}
        region={["CifL_Rzy_Mku"]}
        municipality={["zHxw_uJZ_NNh"]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={resolvedLabels}
        q=""
        sortBy="PublishedAtDesc"
        hasStatedDesiredOccupation
      />,
    );
    expect(screen.getByText("Solna")).toBeInTheDocument();

    await user.click(
      screen.getByRole("button", { name: "Ta bort filter Solna" }),
    );
    // municipality bort, region bevarad.
    expect(pushMock).toHaveBeenCalledWith("/jobb?region=CifL_Rzy_Mku&commit=true");
  });

  it("fallback-label för okänd conceptId", () => {
    render(
      <JobbResultsToolbar
        totalCount={1}
        occupationGroup={[]}
        region={["XX_unknown"]}
        municipality={[]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={{}}
        q=""
        sortBy="PublishedAtDesc"
        hasStatedDesiredOccupation
      />,
    );
    expect(
      screen.getByText("Okänd kod (XX_unknown)"),
    ).toBeInTheDocument();
  });

  it("q-orden visas som taggar med Search-semantik och × tar bort ordet (E2i)", async () => {
    const user = userEvent.setup();
    render(
      <JobbResultsToolbar
        totalCount={3}
        occupationGroup={[]}
        region={[]}
        municipality={[]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={{}}
        q="volvo lastbil"
        sortBy="PublishedAtDesc"
        hasStatedDesiredOccupation
      />,
    );
    expect(screen.getByText("volvo")).toBeInTheDocument();
    expect(screen.getByText("lastbil")).toBeInTheDocument();

    await user.click(
      screen.getByRole("button", { name: "Ta bort sökordet volvo" }),
    );
    expect(pushMock).toHaveBeenCalledWith("/jobb?q=lastbil&commit=true");
  });

  it("Rensa sökord och filter nollar ALLT inkl. q (E2i Klas-beslut — ersätter E2e-domen)", async () => {
    const user = userEvent.setup();
    render(
      <JobbResultsToolbar
        totalCount={3}
        occupationGroup={["MVqp_eS8_kDZ"]}
        region={["CifL_Rzy_Mku"]}
        municipality={["zHxw_uJZ_NNh"]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={resolvedLabels}
        q="backend"
        sortBy="PublishedAtDesc"
        hasStatedDesiredOccupation
      />,
    );
    await user.click(
      screen.getByRole("button", { name: "Rensa sökord och filter" }),
    );
    expect(pushMock).toHaveBeenCalledWith("/jobb?commit=true");
  });

  it("Rensa-länken bevarar icke-default sortBy (E2e, code-reviewer Minor 1)", async () => {
    const user = userEvent.setup();
    render(
      <JobbResultsToolbar
        totalCount={3}
        occupationGroup={["MVqp_eS8_kDZ"]}
        region={[]}
        municipality={[]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={resolvedLabels}
        q="backend"
        sortBy="ExpiresAtAsc"
        hasStatedDesiredOccupation
      />,
    );
    await user.click(
      screen.getByRole("button", { name: "Rensa sökord och filter" }),
    );
    expect(pushMock).toHaveBeenCalledWith("/jobb?sortBy=ExpiresAtAsc&commit=true");
  });

  it("Rensa-länken visas inte utan aktiva chips (E2e)", () => {
    render(
      <JobbResultsToolbar
        totalCount={5}
        occupationGroup={[]}
        region={[]}
        municipality={[]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={{}}
        q=""
        sortBy="PublishedAtDesc"
        hasStatedDesiredOccupation
      />,
    );
    expect(
      screen.queryByRole("button", { name: "Rensa sökord och filter" }),
    ).toBeNull();
  });

  it("sort-alternativen bär labels (Relevans / Sortera efter matchning / Datum (nyast) / Ansökningsdatum (sista ansökan))", () => {
    render(
      <JobbResultsToolbar
        totalCount={5}
        occupationGroup={[]}
        region={[]}
        municipality={[]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={{}}
        q="ab"
        sortBy="PublishedAtDesc"
        hasStatedDesiredOccupation
      />,
    );
    expect(screen.getByRole("option", { name: "Relevans" })).toBeInTheDocument();
    expect(
      screen.getByRole("option", { name: "Sortera efter matchning" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("option", { name: "Datum (nyast)" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("option", { name: "Ansökningsdatum (sista ansökan)" }),
    ).toBeInTheDocument();
  });

  it("match-sort-alternativet visas och är ALDRIG disablat utan söktext (F4-14, till skillnad från Relevance)", () => {
    render(
      <JobbResultsToolbar
        totalCount={5}
        occupationGroup={[]}
        region={[]}
        municipality={[]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={{}}
        q=""
        sortBy="PublishedAtDesc"
        hasStatedDesiredOccupation
      />,
    );
    const opt = screen.getByRole("option", {
      name: "Sortera efter matchning",
    }) as HTMLOptionElement;
    expect(opt.disabled).toBe(false);
  });

  it("match-sort-byte commit:ar sortBy=MatchDesc och bevarar q + filter (F4-14)", async () => {
    const user = userEvent.setup();
    render(
      <JobbResultsToolbar
        totalCount={5}
        occupationGroup={["MVqp_eS8_kDZ"]}
        region={[]}
        municipality={[]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={resolvedLabels}
        q="data"
        sortBy="PublishedAtDesc"
        hasStatedDesiredOccupation
      />,
    );
    await user.selectOptions(
      screen.getByLabelText("Sortera"),
      "Sortera efter matchning",
    );
    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=MVqp_eS8_kDZ&q=data&sortBy=MatchDesc&commit=true",
    );
  });

  it("Relevance-alternativet är disablat utan söktext (ADR 0042 Beslut D)", () => {
    render(
      <JobbResultsToolbar
        totalCount={5}
        occupationGroup={[]}
        region={[]}
        municipality={[]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={{}}
        q=""
        sortBy="PublishedAtDesc"
        hasStatedDesiredOccupation
      />,
    );
    const opt = screen.getByRole("option", {
      name: "Relevans",
    }) as HTMLOptionElement;
    expect(opt.disabled).toBe(true);
  });

  it("Relevance-alternativet är aktivt med q ≥ 2 tecken", () => {
    render(
      <JobbResultsToolbar
        totalCount={5}
        occupationGroup={[]}
        region={[]}
        municipality={[]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={{}}
        q="ab"
        sortBy="PublishedAtDesc"
        hasStatedDesiredOccupation
      />,
    );
    const opt = screen.getByRole("option", {
      name: "Relevans",
    }) as HTMLOptionElement;
    expect(opt.disabled).toBe(false);
  });

  it("sort-byte commit:ar sortBy och bevarar q + filter", async () => {
    const user = userEvent.setup();
    render(
      <JobbResultsToolbar
        totalCount={5}
        occupationGroup={["MVqp_eS8_kDZ"]}
        region={[]}
        municipality={[]}
        employmentType={[]}
        worktimeExtent={[]}
        resolvedLabels={resolvedLabels}
        q="data"
        sortBy="PublishedAtDesc"
        hasStatedDesiredOccupation
      />,
    );
    await user.selectOptions(
      screen.getByLabelText("Sortera"),
      "Relevans",
    );
    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?occupationGroup=MVqp_eS8_kDZ&q=data&sortBy=Relevance&commit=true",
    );
  });

  // F4-16 (CTO D8) — in-/jobb-disclosure.
  describe("in-/jobb-disclosure (match-sort utan angivet yrke)", () => {
    const DISCLOSURE = /Matchningssortering kräver att du anger/;

    it("visas när match-sort är aktiv OCH inget yrke angetts", () => {
      render(
        <JobbResultsToolbar
          totalCount={5}
          occupationGroup={[]}
          region={[]}
          municipality={[]}
          employmentType={[]}
          worktimeExtent={[]}
          resolvedLabels={{}}
          q=""
          sortBy="MatchDesc"
          hasStatedDesiredOccupation={false}
        />,
      );
      expect(screen.getByText(DISCLOSURE)).toBeInTheDocument();
      const link = screen.getByRole("link", { name: "Ställ in matchning" });
      expect(link).toHaveAttribute("href", "/installningar#matchning");
    });

    it("visas INTE när match-sort är aktiv men yrke ÄR angett", () => {
      render(
        <JobbResultsToolbar
          totalCount={5}
          occupationGroup={[]}
          region={[]}
          municipality={[]}
          employmentType={[]}
          worktimeExtent={[]}
          resolvedLabels={{}}
          q=""
          sortBy="MatchDesc"
          hasStatedDesiredOccupation
        />,
      );
      expect(screen.queryByText(DISCLOSURE)).not.toBeInTheDocument();
    });

    it("visas INTE vid annan sort även utan angivet yrke", () => {
      render(
        <JobbResultsToolbar
          totalCount={5}
          occupationGroup={[]}
          region={[]}
          municipality={[]}
          employmentType={[]}
          worktimeExtent={[]}
          resolvedLabels={{}}
          q=""
          sortBy="PublishedAtDesc"
          hasStatedDesiredOccupation={false}
        />,
      );
      expect(screen.queryByText(DISCLOSURE)).not.toBeInTheDocument();
    });

    it("self-clearing: försvinner när sorten inte längre är match-sort (efter navigering)", () => {
      // Self-clearing-mekanismen: när användaren byter sort navigerar URL:en →
      // servern re-renderar JobbResults med nytt sortBy-prop. Här bevisas
      // villkoret direkt på propen (jsdom navigerar inte; optimistic-overlayt
      // är transient och hör till den pågående transitionen).
      const props = {
        totalCount: 5,
        occupationGroup: [] as string[],
        region: [] as string[],
        municipality: [] as string[],
        employmentType: [] as string[],
        worktimeExtent: [] as string[],
        resolvedLabels: {},
        q: "",
        hasStatedDesiredOccupation: false,
      };
      const { rerender } = render(
        <JobbResultsToolbar {...props} sortBy="MatchDesc" />,
      );
      expect(screen.getByText(DISCLOSURE)).toBeInTheDocument();

      rerender(<JobbResultsToolbar {...props} sortBy="PublishedAtDesc" />);
      expect(screen.queryByText(DISCLOSURE)).not.toBeInTheDocument();
    });

    it("self-clearing: försvinner när yrke ställts in (propen blir true)", () => {
      const props = {
        totalCount: 5,
        occupationGroup: [] as string[],
        region: [] as string[],
        municipality: [] as string[],
        employmentType: [] as string[],
        worktimeExtent: [] as string[],
        resolvedLabels: {},
        q: "",
        sortBy: "MatchDesc" as const,
      };
      const { rerender } = render(
        <JobbResultsToolbar {...props} hasStatedDesiredOccupation={false} />,
      );
      expect(screen.getByText(DISCLOSURE)).toBeInTheDocument();

      rerender(<JobbResultsToolbar {...props} hasStatedDesiredOccupation />);
      expect(screen.queryByText(DISCLOSURE)).not.toBeInTheDocument();
    });
  });
});
