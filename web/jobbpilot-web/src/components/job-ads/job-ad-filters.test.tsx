import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobAdFilters } from "./job-ad-filters";
import type { JobAdFiltersValues } from "@/lib/dto/job-ads";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";

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

// ADR 0043 — picker-träd-fixtur. Namn visas i UI; concept-id emitteras till
// URL (Beslut B-kontrakt OFÖRÄNDRAT).
const taxonomy: TaxonomyTree = {
  regions: [
    { conceptId: "CifL_Rzy_Mku", label: "Stockholms län" },
    { conceptId: "oDpK_oQy_3Zc", label: "Västra Götalands län" },
  ],
  occupationFields: [
    {
      conceptId: "apaJ_2ja_LuF",
      label: "Data/IT",
      occupations: [
        { conceptId: "MVqp_eS8_kDZ", label: "Systemutvecklare" },
        { conceptId: "Q5DF_juj_8do", label: "Mjukvaruarkitekt" },
      ],
    },
    {
      conceptId: "X1bg_e2a_ABC",
      label: "Bygg och anläggning",
      occupations: [{ conceptId: "Z9zz_zzz_zzz", label: "Snickare" }],
    },
  ],
};

const emptyLabels = new Map<string, string>();

describe("JobAdFilters (ADR 0042 Beslut A/B/C/D + ADR 0043 namn-väljare)", () => {
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

  // ── IA-regression (ADR 0042 Beslut A) ────────────────────────────────

  it("renders the always-visible search field and a collapsed filter disclosure (Beslut A)", () => {
    render(
      <JobAdFilters
        initial={initial}
        activeFilterCount={0}
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );
    expect(screen.getByLabelText("Sökord")).toBeInTheDocument();
    const disclosure = screen.getByRole("button", { name: /Filter/ });
    expect(disclosure).toHaveAttribute("aria-expanded", "false");
    // Taxonomi-väljarna är inte i DOM förrän disclosuren öppnas.
    expect(screen.queryByLabelText("Län")).not.toBeInTheDocument();
  });

  it("shows Sortering as its own always-visible control, not inside the Filter disclosure (Klas 2026-05-17)", () => {
    render(
      <JobAdFilters
        initial={initial}
        activeFilterCount={0}
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );
    expect(
      screen.getByRole("button", { name: /Filter/ })
    ).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByLabelText("Sortering")).toBeInTheDocument();
    expect(screen.queryByLabelText("Län")).not.toBeInTheDocument();
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
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );
    expect(
      screen.getByRole("button", { name: /Filter \(1 aktiva\)/ })
    ).toHaveAttribute("aria-expanded", "true");
    // ADR 0043 — Yrke-gruppen är synlig (Yrkesområde→Yrke-väljarna), och
    // Län-väljaren. "Yrke" förekommer både som grupprubrik och som under-
    // labels → använd de entydiga select-labels.
    expect(screen.getByLabelText("Yrkesområde")).toBeInTheDocument();
    expect(screen.getByLabelText("Län")).toBeInTheDocument();
  });

  // ── Sök/Återställ + Beslut B URL-multi-kontrakt ──────────────────────

  it("submits q and pushes URL with the search term", async () => {
    const user = userEvent.setup();
    render(
      <JobAdFilters
        initial={initial}
        activeFilterCount={0}
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );

    await user.type(screen.getByLabelText("Sökord"), "backend");
    await user.click(screen.getByRole("button", { name: "Sök" }));

    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith("/jobb?q=backend")
    );
  });

  it("picks occupations by name (Yrkesområde→Yrke) and emits concept-id in the URL (Beslut B unchanged)", async () => {
    const user = userEvent.setup();
    render(
      <JobAdFilters
        initial={initial}
        activeFilterCount={0}
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );

    await user.click(screen.getByRole("button", { name: /Filter/ }));

    // Välj yrkesområde (namn), därefter yrke (namn) — concept-id syns aldrig.
    await user.selectOptions(
      screen.getByLabelText("Yrkesområde"),
      "apaJ_2ja_LuF"
    );
    const occSelect = screen.getByLabelText("Yrke");
    await user.selectOptions(occSelect, "MVqp_eS8_kDZ");
    await user.selectOptions(occSelect, "Q5DF_juj_8do");

    // Chips visar NAMN, inte concept-id.
    expect(screen.getByText("Systemutvecklare")).toBeInTheDocument();
    expect(screen.getByText("Mjukvaruarkitekt")).toBeInTheDocument();
    expect(screen.queryByText("MVqp_eS8_kDZ")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Sök" }));

    // URL bär fortfarande concept-id (ADR 0042 Beslut B OFÖRÄNDRAT).
    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith(
        "/jobb?ssyk=MVqp_eS8_kDZ&ssyk=Q5DF_juj_8do"
      )
    );
  });

  it("picks a län by name and emits its concept-id as repeated region param", async () => {
    const user = userEvent.setup();
    render(
      <JobAdFilters
        initial={initial}
        activeFilterCount={0}
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );

    await user.click(screen.getByRole("button", { name: /Filter/ }));
    await user.selectOptions(screen.getByLabelText("Län"), "CifL_Rzy_Mku");

    expect(screen.getByText("Stockholms län")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Sök" }));

    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith("/jobb?region=CifL_Rzy_Mku")
    );
  });

  it("renders an already-selected ssyk concept-id as its name via the tree", () => {
    render(
      <JobAdFilters
        initial={{ ...initial, ssyk: ["MVqp_eS8_kDZ"] }}
        activeFilterCount={1}
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );
    expect(screen.getByText("Systemutvecklare")).toBeInTheDocument();
    expect(screen.queryByText("MVqp_eS8_kDZ")).not.toBeInTheDocument();
  });

  it("falls back to the reverse-lookup label for a saved id not in the tree", () => {
    const resolved = new Map<string, string>([
      ["GONE_id_001", "Tidigare yrkesnamn"],
    ]);
    render(
      <JobAdFilters
        initial={{ ...initial, ssyk: ["GONE_id_001"] }}
        activeFilterCount={1}
        taxonomy={taxonomy}
        resolvedLabels={resolved}
      />
    );
    expect(screen.getByText("Tidigare yrkesnamn")).toBeInTheDocument();
  });

  it("removes a selected taxonomy chip and pushes plain /jobb", async () => {
    const user = userEvent.setup();
    render(
      <JobAdFilters
        initial={{ ...initial, ssyk: ["MVqp_eS8_kDZ"] }}
        activeFilterCount={1}
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );

    await user.click(
      screen.getByRole("button", { name: "Ta bort Systemutvecklare" })
    );
    await user.click(screen.getByRole("button", { name: "Sök" }));

    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/jobb"));
  });

  it("rejects q with 1 char and shows error (mirrors backend validator)", async () => {
    const user = userEvent.setup();
    render(
      <JobAdFilters
        initial={initial}
        activeFilterCount={0}
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );

    await user.type(screen.getByLabelText("Sökord"), "a");
    await user.click(screen.getByRole("button", { name: "Sök" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /Söktexten måste vara 2–100 tecken/
    );
    expect(pushMock).not.toHaveBeenCalled();
  });

  it("disables the Relevance sort option until a search term is present (Beslut D)", async () => {
    const user = userEvent.setup();
    render(
      <JobAdFilters
        initial={initial}
        activeFilterCount={0}
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );

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
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );

    await user.click(screen.getByRole("button", { name: "Återställ" }));
    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/jobb"));
  });

  // ── Graceful degradation när trädet inte kunde laddas ────────────────

  it("degrades civilly when the taxonomy tree failed to load (null)", async () => {
    const user = userEvent.setup();
    render(
      <JobAdFilters
        initial={initial}
        activeFilterCount={0}
        taxonomy={null}
        resolvedLabels={emptyLabels}
      />
    );

    await user.click(screen.getByRole("button", { name: /Filter/ }));

    expect(
      screen.getByText(/Län- och yrkesval kunde inte laddas just nu/)
    ).toBeInTheDocument();
    // Sök-på-sökord fungerar fortfarande.
    await user.type(screen.getByLabelText("Sökord"), "data");
    await user.click(screen.getByRole("button", { name: "Sök" }));
    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith("/jobb?q=data")
    );
  });
});
