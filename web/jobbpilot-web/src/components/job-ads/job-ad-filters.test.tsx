import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobAdFilters } from "./job-ad-filters";
import type { JobAdFiltersValues } from "@/lib/dto/job-ads";

const pushMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

const initial: JobAdFiltersValues = {
  ssyk: [],
  region: [],
  q: "",
  sortBy: "PublishedAtDesc",
};

describe("JobAdFilters (ADR 0042 Beslut A/B/C/D)", () => {
  beforeEach(() => {
    pushMock.mockReset();
    // Typeahead-proxy: default tom lista så inga förslag dyker upp i tester
    // som inte testar typeahead.
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response("[]", { status: 200 }))
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders the always-visible search field and a collapsed filter disclosure (Beslut A)", () => {
    render(<JobAdFilters initial={initial} activeFilterCount={0} />);
    expect(screen.getByLabelText("Sökord")).toBeInTheDocument();
    const disclosure = screen.getByRole("button", { name: /Filter/ });
    expect(disclosure).toHaveAttribute("aria-expanded", "false");
    // Taxonomi-fält är inte i DOM förrän disclosuren öppnas.
    expect(screen.queryByLabelText("Yrkesområde")).not.toBeInTheDocument();
  });

  it("shows Sortering as its own always-visible control, not inside the Filter disclosure (Klas 2026-05-17)", () => {
    render(<JobAdFilters initial={initial} activeFilterCount={0} />);
    // Sortering är synlig direkt — disclosuren är stängd.
    expect(
      screen.getByRole("button", { name: /Filter/ })
    ).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByLabelText("Sortering")).toBeInTheDocument();
    // Taxonomi-fälten ligger kvar bakom disclosuren.
    expect(screen.queryByLabelText("Yrkesområde")).not.toBeInTheDocument();
    // Nya, tydligare etiketter (ingen dubbel-"sista").
    expect(
      screen.getByRole("option", { name: "Stänger snart" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("option", { name: "Stänger senare" })
    ).toBeInTheDocument();
  });

  it("auto-expands the disclosure when filters are already active", () => {
    render(
      <JobAdFilters
        initial={{ ...initial, ssyk: ["MVqp_eS8_kDZ"] }}
        activeFilterCount={1}
      />
    );
    expect(
      screen.getByRole("button", { name: /Filter \(1 aktiva\)/ })
    ).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByLabelText("Yrkesområde")).toBeInTheDocument();
  });

  it("submits q and pushes URL with the search term", async () => {
    const user = userEvent.setup();
    render(<JobAdFilters initial={initial} activeFilterCount={0} />);

    await user.type(screen.getByLabelText("Sökord"), "backend");
    await user.click(screen.getByRole("button", { name: "Sök" }));

    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/jobb?q=backend"));
  });

  it("adds multiple ssyk values as repeated query params (Beslut B)", async () => {
    const user = userEvent.setup();
    render(<JobAdFilters initial={initial} activeFilterCount={0} />);

    await user.click(screen.getByRole("button", { name: /Filter/ }));

    const ssykField = screen.getByLabelText("Yrkesområde");
    await user.type(ssykField, "MVqp_eS8_kDZ");
    await user.click(
      screen.getAllByRole("button", { name: "Lägg till" })[0]!
    );
    await user.type(ssykField, "CifL_Rzy_Mku");
    await user.click(
      screen.getAllByRole("button", { name: "Lägg till" })[0]!
    );

    await user.click(screen.getByRole("button", { name: "Sök" }));

    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith(
        "/jobb?ssyk=MVqp_eS8_kDZ&ssyk=CifL_Rzy_Mku"
      )
    );
  });

  it("removes a selected taxonomy chip", async () => {
    const user = userEvent.setup();
    render(
      <JobAdFilters
        initial={{ ...initial, ssyk: ["MVqp_eS8_kDZ"] }}
        activeFilterCount={1}
      />
    );

    await user.click(
      screen.getByRole("button", { name: "Ta bort MVqp_eS8_kDZ" })
    );
    await user.click(screen.getByRole("button", { name: "Sök" }));

    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/jobb"));
  });

  it("rejects q with 1 char and shows error (mirrors backend validator)", async () => {
    const user = userEvent.setup();
    render(<JobAdFilters initial={initial} activeFilterCount={0} />);

    await user.type(screen.getByLabelText("Sökord"), "a");
    await user.click(screen.getByRole("button", { name: "Sök" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /Söktexten måste vara 2–100 tecken/
    );
    expect(pushMock).not.toHaveBeenCalled();
  });

  it("disables the Relevance sort option until a search term is present (Beslut D)", async () => {
    const user = userEvent.setup();
    render(<JobAdFilters initial={initial} activeFilterCount={0} />);
    // Sortering är nu en egen alltid-synlig kontroll (Klas 2026-05-17) —
    // behöver inte längre öppna Filter-disclosuren för att nå den.

    const relevance = screen.getByRole("option", {
      name: "Mest relevant",
    }) as HTMLOptionElement;
    expect(relevance.disabled).toBe(true);

    await user.type(screen.getByLabelText("Sökord"), "java");
    await waitFor(() => expect(relevance.disabled).toBe(false));
  });

  it("Återställ pushes plain /jobb", async () => {
    const user = userEvent.setup();
    render(
      <JobAdFilters
        initial={{ ...initial, q: "backend", ssyk: ["MVqp_eS8_kDZ"] }}
        activeFilterCount={2}
      />
    );

    await user.click(screen.getByRole("button", { name: "Återställ" }));
    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/jobb"));
  });
});
