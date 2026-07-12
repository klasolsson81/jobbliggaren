import { describe, it, expect, vi, beforeEach } from "vitest";
import { useState } from "react";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { TaxonomyRegion } from "@/lib/dto/taxonomy";
import type { WatchFilter } from "@/lib/dto/company-follows";

// The dialog's save is a "use server" action (next/cache + server-only); stub the module so the
// client component imports cleanly and the SAVED PAYLOAD is observable — the payload is the contract.
const { setWatchFilterMock } = vi.hoisted(() => ({ setWatchFilterMock: vi.fn() }));
vi.mock("@/lib/actions/company-follows", () => ({
  setWatchFilterAction: setWatchFilterMock,
  unfollowCompanyAction: vi.fn(),
}));

import { WatchFilterDialog } from "./watch-filter-dialog";

// Two län with disjoint kommun ids. The two axes are SEPARATE JobTech namespaces: `r_*` ids only
// ever belong in `regions`, `m_*` ids only ever in `municipalities`.
const REGIONS: ReadonlyArray<TaxonomyRegion> = [
  {
    conceptId: "r_sthlm",
    label: "Stockholms län",
    municipalities: [
      { conceptId: "m_sthlm", label: "Stockholm" },
      { conceptId: "m_solna", label: "Solna" },
    ],
  },
  {
    conceptId: "r_vg",
    label: "Västra Götalands län",
    municipalities: [{ conceptId: "m_gbg", label: "Göteborg" }],
  },
];

const WATCH_ID = "11111111-1111-1111-1111-111111111111";
const ONLY_MATCHED = "Endast annonser som matchar min profil";
const SAVE = "Spara filter";
const CLEAR = "Ta bort filtret";
const DIALOG_TITLE = "Filtrera annonser från Skatteverket";

/**
 * Mirrors the ROW's ownership of the dialog: the row mounts it only while open (`{filterOpen && …}`)
 * and hands it a `setOpen`. Modelling that here is what makes "closes on success" / "stays open on
 * failure" honest observations — with a hard-coded `open` prop the dialog could never disappear and
 * the #141 pin would be vacuous.
 */
function Host({
  filter = null,
  matchingNotAssessed = false,
  regions = REGIONS,
}: {
  filter?: WatchFilter | null;
  matchingNotAssessed?: boolean;
  regions?: ReadonlyArray<TaxonomyRegion>;
}) {
  const [open, setOpen] = useState(true);
  return open ? (
    <WatchFilterDialog
      open={open}
      onOpenChange={setOpen}
      companyWatchId={WATCH_ID}
      companyName="Skatteverket"
      filter={filter}
      regions={regions}
      matchingNotAssessed={matchingNotAssessed}
    />
  ) : (
    <p>dialogen är stängd</p>
  );
}

/** The payload of the (single) save call. */
function savedPayload(): unknown {
  expect(setWatchFilterMock).toHaveBeenCalledTimes(1);
  return setWatchFilterMock.mock.calls[0]![1];
}

async function openCascadeRegion(user: ReturnType<typeof userEvent.setup>, lan: string) {
  await user.click(screen.getByRole("button", { name: "Lägg till orter" }));
  await user.click(screen.getByRole("button", { name: lan }));
}

beforeEach(() => {
  setWatchFilterMock.mockReset();
  setWatchFilterMock.mockResolvedValue({ success: true });
});

describe("WatchFilterDialog — the two geo axes never cross (the load-bearing wire pin)", () => {
  it("'Hela länet' sparas som ETT läns-id i regions — aldrig expanderat till länets kommuner", async () => {
    // THE regression this pins: someone "helpfully" expands a whole-län pick into the län's ~49
    // kommun-ids. An ad tagged at LÄN granularity carries no municipality at all, so the expanded
    // filter would stop notifying about exactly those ads — silently, with the user seeing a filter
    // that looks right. The län concept-id must travel verbatim, in the region axis.
    const user = userEvent.setup();
    render(<Host />);

    await openCascadeRegion(user, "Stockholms län");
    await user.click(screen.getByRole("checkbox", { name: "Hela Stockholms län" }));
    await user.click(screen.getByRole("button", { name: SAVE }));

    await waitFor(() => expect(setWatchFilterMock).toHaveBeenCalled());
    expect(savedPayload()).toEqual({
      municipalities: [],
      regions: ["r_sthlm"],
      onlyMatched: false,
    });
  });

  it("ett kommun-val sparas i municipalities — aldrig i regions (ingen korsad axel)", async () => {
    // The mirror regression: a kommun id written into `regions` would be looked up in the län
    // namespace, match nothing, and suppress every notification for the watch.
    const user = userEvent.setup();
    render(<Host />);

    await openCascadeRegion(user, "Stockholms län");
    await user.click(screen.getByRole("checkbox", { name: "Solna" }));
    await user.click(screen.getByRole("button", { name: SAVE }));

    await waitFor(() => expect(setWatchFilterMock).toHaveBeenCalled());
    expect(savedPayload()).toEqual({
      municipalities: ["m_solna"],
      regions: [],
      onlyMatched: false,
    });
  });

  it("ett län OCH en kommun i ett annat län reser i var sin axel", async () => {
    // Västra Götaland picked WHOLE (→ region axis), Solna picked as a single kommun in another län
    // (→ municipality axis). The two must arrive side by side, each in its own namespace — this is
    // the shape a real user produces, and the one an axis-crossing bug would scramble.
    const user = userEvent.setup();
    render(<Host />);

    await openCascadeRegion(user, "Västra Götalands län");
    await user.click(
      screen.getByRole("checkbox", { name: "Hela Västra Götalands län" })
    );
    await user.click(screen.getByRole("button", { name: "Stockholms län" }));
    await user.click(screen.getByRole("checkbox", { name: "Solna" }));
    await user.click(screen.getByRole("button", { name: SAVE }));

    await waitFor(() => expect(setWatchFilterMock).toHaveBeenCalled());
    expect(savedPayload()).toEqual({
      municipalities: ["m_solna"],
      regions: ["r_vg"],
      onlyMatched: false,
    });
  });

  it("sparar bevakningens id (opakt) — aldrig org.nr", async () => {
    const user = userEvent.setup();
    render(<Host filter={{ municipalities: [], regions: [], onlyMatched: true }} />);

    await user.click(screen.getByRole("button", { name: SAVE }));

    await waitFor(() => expect(setWatchFilterMock).toHaveBeenCalled());
    expect(setWatchFilterMock.mock.calls[0]![0]).toBe(WATCH_ID);
  });
});

describe("WatchFilterDialog — draften seedas ur det persisterade filtret", () => {
  it("förvalda län + kommuner renderas som chips, och 'endast matchande' är ikryssad", () => {
    render(
      <Host
        filter={{
          municipalities: ["m_gbg"],
          regions: ["r_sthlm"],
          onlyMatched: true,
        }}
      />
    );

    expect(screen.getByRole("checkbox", { name: ONLY_MATCHED })).toHaveAttribute(
      "aria-checked",
      "true"
    );
    const pinned = screen.getByRole("list", { name: "Valda orter" });
    expect(
      within(pinned).getByRole("button", { name: "Ta bort Stockholms län" })
    ).toBeInTheDocument();
    expect(
      within(pinned).getByRole("button", { name: "Ta bort Göteborg" })
    ).toBeInTheDocument();
  });

  it("ett oförändrat, förvalt filter sparas oförvanskat (round-trip: läst → skrivet)", async () => {
    // Pins the read↔write symmetry: what the list read gives us is exactly what a no-op save sends
    // back. A dropped or re-homed axis in the seeding would show up here as a silently narrowed filter.
    const user = userEvent.setup();
    render(
      <Host
        filter={{ municipalities: ["m_gbg"], regions: ["r_sthlm"], onlyMatched: true }}
      />
    );

    await user.click(screen.getByRole("button", { name: SAVE }));

    await waitFor(() => expect(setWatchFilterMock).toHaveBeenCalled());
    expect(savedPayload()).toEqual({
      municipalities: ["m_gbg"],
      regions: ["r_sthlm"],
      onlyMatched: true,
    });
  });
});

describe("WatchFilterDialog — ett tomt val RENSAR filtret", () => {
  it("'Ta bort filtret' + Spara skickar ett all-tomt val (och ger INGET valideringsfel)", async () => {
    // "Jag vill inte filtrera längre" is the natural action, and the backend maps the empty selection
    // to the canonical NULL. If the UI turned it into a validation error the user would be stuck with
    // an active filter and no way out — the F4a `[""]`-bug, one layer up.
    const user = userEvent.setup();
    render(
      <Host
        filter={{ municipalities: ["m_gbg"], regions: ["r_sthlm"], onlyMatched: true }}
      />
    );

    await user.click(screen.getByRole("button", { name: CLEAR }));
    await user.click(screen.getByRole("button", { name: SAVE }));

    await waitFor(() => expect(setWatchFilterMock).toHaveBeenCalled());
    expect(savedPayload()).toEqual({
      municipalities: [],
      regions: [],
      onlyMatched: false,
    });
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("avbocka 'endast matchande' (enda filtret) + Spara skickar också ett all-tomt val", async () => {
    // The clear path must not depend on the "Ta bort filtret"-link existing: unchecking the last
    // control IS an empty selection.
    const user = userEvent.setup();
    render(<Host filter={{ municipalities: [], regions: [], onlyMatched: true }} />);

    await user.click(screen.getByRole("checkbox", { name: ONLY_MATCHED }));
    await user.click(screen.getByRole("button", { name: SAVE }));

    await waitFor(() => expect(setWatchFilterMock).toHaveBeenCalled());
    expect(savedPayload()).toEqual({
      municipalities: [],
      regions: [],
      onlyMatched: false,
    });
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("'Ta bort filtret' visas bara när draften bär något (ingen död kontroll)", async () => {
    const user = userEvent.setup();
    render(<Host />);

    // Tom draft (inget persisterat filter) → ingen rensa-länk.
    expect(screen.queryByRole("button", { name: CLEAR })).toBeNull();

    await user.click(screen.getByRole("checkbox", { name: ONLY_MATCHED }));

    expect(screen.getByRole("button", { name: CLEAR })).toBeInTheDocument();
  });

  it("'Ta bort filtret' rensar DRAFTEN — den sparar inte av sig själv", async () => {
    // The only commit boundary is "Spara filter", so a mis-click is undoable with Avbryt.
    const user = userEvent.setup();
    render(<Host filter={{ municipalities: [], regions: [], onlyMatched: true }} />);

    await user.click(screen.getByRole("button", { name: CLEAR }));

    expect(setWatchFilterMock).not.toHaveBeenCalled();
    expect(screen.getByRole("checkbox", { name: ONLY_MATCHED })).toHaveAttribute(
      "aria-checked",
      "false"
    );
  });
});

describe("WatchFilterDialog — 'endast matchande' låses ALDRIG (CTO Q8-b)", () => {
  it("utan matchningsprofil är kontrollen fortfarande operabel OCH sparas som true", async () => {
    // The backend accepts the value and holds the filter INERT until an occupation is stated — 8C's
    // read-time grading is chosen precisely so the filter starts working the moment the profile
    // exists, without the user having to come back here. A disabled control would make that
    // impossible to express, and would be an FE-invented rule the backend does not have. A test that
    // asserted `disabled` would encode the WRONG behaviour; this one pins the right one.
    const user = userEvent.setup();
    render(<Host matchingNotAssessed />);

    const check = screen.getByRole("checkbox", { name: ONLY_MATCHED });
    expect(check).toHaveAttribute("aria-checked", "false");

    await user.click(check);
    expect(check).toHaveAttribute("aria-checked", "true");

    await user.click(screen.getByRole("button", { name: SAVE }));
    await waitFor(() => expect(setWatchFilterMock).toHaveBeenCalled());
    expect(savedPayload()).toEqual({
      municipalities: [],
      regions: [],
      onlyMatched: true,
    });
  });

  /** Löser upp aria-describedby (som kan bära FLERA id) till den text en skärmläsare faktiskt läser. */
  function describedText(el: HTMLElement): string {
    const ids = (el.getAttribute("aria-describedby") ?? "").split(/\s+/).filter(Boolean);
    expect(ids.length).toBeGreaterThan(0);
    return ids
      .map((id) => {
        const node = document.getElementById(id);
        expect(node).not.toBeNull();
        return node?.textContent ?? "";
      })
      .join(" ");
  }

  it("den inerta nudgen är KOPPLAD till kontrollen (aria-describedby), inte bara placerad bredvid", () => {
    // Kontrollen låses aldrig (se testet ovan) — då måste skälet nå en skärmläsar-användare via
    // kontrollen SJÄLV. I forms-mode läses bara namnet + beskrivningen: en text som bara står bredvid
    // hörs aldrig. Utan kopplingen kryssar användaren i, sparar, och får ett inert filter utan att
    // någonsin få veta varför — den tysta smalningen igen, ett lager ned.
    render(<Host matchingNotAssessed />);

    const described = describedText(
      screen.getByRole("checkbox", { name: ONLY_MATCHED })
    );
    // Båda halvorna hörs: vad kontrollen gör, OCH varför den inte gäller än.
    expect(described).toMatch(/matchar de yrken, orter och kompetenser/);
    expect(described).toMatch(/Filtret sparas och börjar gälla/);
  });

  it("med matchningsprofil beskrivs kontrollen av hjälptexten, utan dinglande inert-skäl", () => {
    render(<Host />);

    const described = describedText(
      screen.getByRole("checkbox", { name: ONLY_MATCHED })
    );
    expect(described).toMatch(/matchar de yrken, orter och kompetenser/);
    // Inget inert-skäl att annonsera när filtret faktiskt gäller.
    expect(described).not.toMatch(/Filtret sparas och börjar gälla/);
  });

  it("utan matchningsprofil visas en ärlig inert-nudge som pekar på matchnings-inställningarna", () => {
    render(<Host matchingNotAssessed />);

    expect(
      screen.getByText(
        "Du har inte angett vilka yrken du söker inom, så vi kan inte avgöra vilka annonser som matchar dig. Filtret sparas och börjar gälla när du ställt in matchningen."
      )
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ställ in matchning" })).toHaveAttribute(
      "href",
      "/installningar#matchning"
    );
  });

  it("med matchningsprofil visas INGEN nudge (den vore en lögn)", () => {
    render(<Host />);

    expect(screen.queryByRole("link", { name: "Ställ in matchning" })).toBeNull();
  });
});

describe("WatchFilterDialog — save-utfallet (#141-fällan)", () => {
  it("lyckad save STÄNGER dialogen (revalidaten får inte avmontera den mitt i flödet)", async () => {
    // #141: a Server Action re-renders the RSC tree, which unmounts an open dialog mid-flow. The
    // dialog therefore closes itself BEFORE the revalidate lands. If the close were dropped the user
    // would be left staring at a dialog that had already saved.
    const user = userEvent.setup();
    render(<Host />);

    await user.click(screen.getByRole("checkbox", { name: ONLY_MATCHED }));
    await user.click(screen.getByRole("button", { name: SAVE }));

    await waitFor(() =>
      expect(screen.getByText("dialogen är stängd")).toBeInTheDocument()
    );
    expect(screen.queryByRole("heading", { name: DIALOG_TITLE })).toBeNull();
  });

  it("misslyckad save håller dialogen ÖPPEN med ett inline role=alert och draften intakt", async () => {
    // The error belongs where the user's work is. Closing on failure would discard the draft AND
    // leave the user believing the filter was saved.
    const user = userEvent.setup();
    setWatchFilterMock.mockResolvedValue({
      success: false,
      error: "Filtret kunde inte sparas. Försök igen.",
    });
    render(<Host />);

    await openCascadeRegion(user, "Stockholms län");
    await user.click(screen.getByRole("checkbox", { name: "Solna" }));
    await user.click(screen.getByRole("button", { name: SAVE }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Filtret kunde inte sparas"
    );
    // Fortfarande öppen …
    expect(screen.getByText(DIALOG_TITLE)).toBeInTheDocument();
    expect(screen.queryByText("dialogen är stängd")).toBeNull();
    // … och draften lever kvar (Solna-chipen finns, så ett nytt försök inte kostar om-valet).
    const pinned = screen.getByRole("list", { name: "Valda orter" });
    expect(
      within(pinned).getByRole("button", { name: "Ta bort Solna" })
    ).toBeInTheDocument();
  });

  it("Avbryt stänger utan att spara", async () => {
    const user = userEvent.setup();
    render(<Host />);

    await user.click(screen.getByRole("checkbox", { name: ONLY_MATCHED }));
    await user.click(screen.getByRole("button", { name: "Avbryt" }));

    await waitFor(() =>
      expect(screen.getByText("dialogen är stängd")).toBeInTheDocument()
    );
    expect(setWatchFilterMock).not.toHaveBeenCalled();
  });

  it("taxonomin kunde inte läsas in → pickern degraderar civilt, filtret är fortfarande sparbart", async () => {
    // /foretag passes `regions: []` when the taxonomy read fails. The ort axis is then unavailable,
    // but "endast matchande" must still be settable — a failed taxonomy read may not take the whole
    // filter down with it.
    const user = userEvent.setup();
    render(<Host regions={[]} />);

    await user.click(screen.getByRole("checkbox", { name: ONLY_MATCHED }));
    await user.click(screen.getByRole("button", { name: SAVE }));

    await waitFor(() => expect(setWatchFilterMock).toHaveBeenCalled());
    expect(savedPayload()).toEqual({
      municipalities: [],
      regions: [],
      onlyMatched: true,
    });
  });
});
