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
      />,
    );
    expect(
      screen.queryByRole("button", { name: "Rensa sökord och filter" }),
    ).toBeNull();
  });

  it("sort-alternativen bär E2e-labels (Relevans / Datum (nyast) / Ansökningsdatum (sista ansökan))", () => {
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
      />,
    );
    expect(screen.getByRole("option", { name: "Relevans" })).toBeInTheDocument();
    expect(
      screen.getByRole("option", { name: "Datum (nyast)" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("option", { name: "Ansökningsdatum (sista ansökan)" }),
    ).toBeInTheDocument();
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
});
