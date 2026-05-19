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
};

beforeEach(() => {
  pushMock.mockClear();
});

describe("JobbResultsToolbar — träffar + chips + sort", () => {
  it("visar mono-formaterat antal träffar", () => {
    render(
      <JobbResultsToolbar
        totalCount={1234}
        ssyk={[]}
        region={[]}
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
        ssyk={["MVqp_eS8_kDZ"]}
        region={["CifL_Rzy_Mku"]}
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
    // region bort, ssyk + q bevarade.
    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?ssyk=MVqp_eS8_kDZ&q=backend",
    );
  });

  it("fallback-label för okänd conceptId", () => {
    render(
      <JobbResultsToolbar
        totalCount={1}
        ssyk={[]}
        region={["XX_unknown"]}
        resolvedLabels={{}}
        q=""
        sortBy="PublishedAtDesc"
      />,
    );
    expect(
      screen.getByText("Okänd kod (XX_unknown)"),
    ).toBeInTheDocument();
  });

  it("Relevance-alternativet är disablat utan söktext (ADR 0042 Beslut D)", () => {
    render(
      <JobbResultsToolbar
        totalCount={5}
        ssyk={[]}
        region={[]}
        resolvedLabels={{}}
        q=""
        sortBy="PublishedAtDesc"
      />,
    );
    const opt = screen.getByRole("option", {
      name: "Mest relevant (CV-match)",
    }) as HTMLOptionElement;
    expect(opt.disabled).toBe(true);
  });

  it("Relevance-alternativet är aktivt med q ≥ 2 tecken", () => {
    render(
      <JobbResultsToolbar
        totalCount={5}
        ssyk={[]}
        region={[]}
        resolvedLabels={{}}
        q="ab"
        sortBy="PublishedAtDesc"
      />,
    );
    const opt = screen.getByRole("option", {
      name: "Mest relevant (CV-match)",
    }) as HTMLOptionElement;
    expect(opt.disabled).toBe(false);
  });

  it("sort-byte commit:ar sortBy och bevarar q + filter", async () => {
    const user = userEvent.setup();
    render(
      <JobbResultsToolbar
        totalCount={5}
        ssyk={["MVqp_eS8_kDZ"]}
        region={[]}
        resolvedLabels={resolvedLabels}
        q="data"
        sortBy="PublishedAtDesc"
      />,
    );
    await user.selectOptions(
      screen.getByLabelText("Sortera"),
      "Mest relevant (CV-match)",
    );
    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?ssyk=MVqp_eS8_kDZ&q=data&sortBy=Relevance",
    );
  });
});
