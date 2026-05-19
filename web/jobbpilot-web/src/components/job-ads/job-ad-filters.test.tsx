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

  it("renders Sortering + a collapsed filter disclosure and owns NO q text input (F3 B-FIX — q ägs av hero-formuläret)", () => {
    render(
      <JobAdFilters
        initial={initial}
        activeFilterCount={0}
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );
    // F3 B-FIX — det fanns tidigare TVÅ auktoritativa q-input-ytor (hero +
    // detta typeahead-fält) bundna till samma `q`-searchParam. Denna form
    // får inte längre rendera något fritext-q-fält (ADR 0047 task-blocker).
    expect(screen.queryByLabelText("Sökord")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Sortering")).toBeInTheDocument();
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

  it("carries the hero search term (initial.q) through a taxonomy/sort submit without dropping it from the URL (F3 B-FIX q-bevarande)", async () => {
    const user = userEvent.setup();
    render(
      <JobAdFilters
        initial={{ ...initial, q: "backend" }}
        activeFilterCount={0}
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );

    await user.click(screen.getByRole("button", { name: /Filter/ }));
    await user.selectOptions(screen.getByLabelText("Län"), "CifL_Rzy_Mku");
    await user.click(screen.getByRole("button", { name: "Sök" }));

    // q (från initial.q) MÅSTE bevaras i URL:en när användaren filtrerar —
    // annars förlorar hen sitt hero-sökord vid taxonomi-ändring.
    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith(
        "/jobb?region=CifL_Rzy_Mku&q=backend"
      )
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

  // ── Relevance-sort-gate (ADR 0042 Beslut D — icke-förhandlingsbar) ───
  // F3 B-FIX: gaten härleds nu från initial.q (searchParam-prop), inte
  // från lokal q-state (denna form har ingen q-input längre). Invarianten
  // FÅR EJ regressa: Relevance får aldrig erbjudas utan söktext ≥2 tecken.

  it("disables Relevance when initial.q is absent (Beslut D — derived from searchParam, no local q)", () => {
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
  });

  it("disables Relevance when initial.q is too short (<2 chars)", () => {
    render(
      <JobAdFilters
        initial={{ ...initial, q: "a" }}
        activeFilterCount={0}
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );

    const relevance = screen.getByRole("option", {
      name: "Mest relevant",
    }) as HTMLOptionElement;
    expect(relevance.disabled).toBe(true);
  });

  it("enables Relevance when initial.q has a valid search term (≥2 chars)", () => {
    render(
      <JobAdFilters
        initial={{ ...initial, q: "java" }}
        activeFilterCount={0}
        taxonomy={taxonomy}
        resolvedLabels={emptyLabels}
      />
    );

    const relevance = screen.getByRole("option", {
      name: "Mest relevant",
    }) as HTMLOptionElement;
    expect(relevance.disabled).toBe(false);
  });

  it("Återställ rensar taxonomi/sort men BEVARAR hero-sökordet q i URL:en (F3 B-FIX — q ägs av hero)", async () => {
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
    // Återställ gäller filtren denna form äger; q ägs av hero → bevaras.
    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith("/jobb?q=backend")
    );
  });

  it("Återställ pushes plain /jobb when there is no hero search term", async () => {
    const user = userEvent.setup();
    render(
      <JobAdFilters
        initial={{ ...initial, ssyk: ["MVqp_eS8_kDZ"] }}
        activeFilterCount={1}
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
    // Formen fungerar ändå: Sök bär vidare ev. hero-q + sortering utan
    // att taxonomi-trädet behövs (q-fältet ägs av hero, ej denna form).
    await user.click(screen.getByRole("button", { name: "Sök" }));
    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/jobb"));
  });
});
