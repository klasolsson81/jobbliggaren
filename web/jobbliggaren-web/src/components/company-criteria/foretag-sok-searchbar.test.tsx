import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { renderToString } from "react-dom/server";
import { NextIntlClientProvider } from "next-intl";
import userEvent from "@testing-library/user-event";
import svPages from "../../../messages/sv/pages.json";
import { ForetagSokSearchbar } from "./foretag-sok-searchbar";
import { buildForetagSokHref } from "@/lib/company-search/search-params";
import type { CriterionReference } from "@/lib/dto/company-criteria";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

const followActionMock = vi.fn();
const unfollowActionMock = vi.fn();
vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyAction: (...args: unknown[]) => followActionMock(...args),
  unfollowCompanyAction: (...args: unknown[]) => unfollowActionMock(...args),
}));

// A small but structurally-real SCB reference: section J with two divisions (so a division's leaf-set is
// a strict subset of the section's — the seed test can match a division unambiguously), and one län.
const REFERENCE: CriterionReference = {
  sniVersion: "2025",
  kommunVersion: "2025",
  sni: [
    {
      code: "J",
      name: "Informations- och kommunikationsverksamhet",
      divisions: [
        {
          code: "62",
          name: "Dataprogrammering, datakonsultverksamhet",
          leaves: [
            { code: "62010", name: "Datakonsultverksamhet" },
            { code: "62020", name: "Systemutveckling och programvarutveckling" },
          ],
        },
        {
          code: "63",
          name: "Informationstjänster",
          leaves: [
            { code: "63110", name: "Databehandling och hosting" },
            { code: "63120", name: "Webbportaler" },
          ],
        },
      ],
    },
  ],
  lan: [
    {
      code: "01",
      name: "Stockholms län",
      kommuner: [
        { code: "0180", name: "Stockholm" },
        { code: "0181", name: "Södertälje" },
      ],
    },
  ],
};

const VALID_ORGNR = "5560125790"; // 3rd digit 6 >= 2 → legal entity
const PNR_SHAPED = "1010101010"; // 3rd digit 1 < 2 → personnummer-shaped → must be refused locally

const FOUND_COMPANY = {
  organizationNumber: VALID_ORGNR,
  isProtectedIdentity: false,
  name: "Volvo AB",
  seatMunicipalityCode: "1480",
  seatMunicipalityName: "Göteborg",
  sniCodes: ["29100"],
};

function orgNrResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const originalFetch = global.fetch;

beforeEach(() => {
  push.mockReset();
  followActionMock.mockReset();
  unfollowActionMock.mockReset();
});
afterEach(() => {
  global.fetch = originalFetch;
  vi.restoreAllMocks();
});

function renderBar(
  props: Partial<React.ComponentProps<typeof ForetagSokSearchbar>> = {},
) {
  return render(
    <ForetagSokSearchbar
      reference={REFERENCE}
      referenceOk
      namn=""
      sni={[]}
      kommun={[]}
      {...props}
    />,
  );
}

/**
 * Open the bransch popover and return its dialog. The trigger owns the open state, exactly as the ort
 * trigger beside it does; the panel's accessible name matches the trigger's visible text.
 */
async function openBransch(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: "Välj bransch" }));
  return screen.getByRole("dialog", { name: "Välj bransch" });
}

/** Filter inside the open popover and tick the row with `name` (any level). */
async function pickBransch(
  user: ReturnType<typeof userEvent.setup>,
  query: string,
  name: string,
) {
  const dialog = await openBransch(user);
  await user.type(within(dialog).getByLabelText("Sök bransch"), query);
  await user.click(within(dialog).getByRole("checkbox", { name }));
  await user.keyboard("{Escape}");
}

describe("ForetagSokSearchbar — one shared draft, one submit", () => {
  it("commits name + bransch + ort TOGETHER on one submit (no silent draft drop)", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    // Seed one applied ort so the ort draft is present without opening the popover.
    renderBar({ kommun: ["0180"] });
    const user = userEvent.setup();

    // The seeded ort chip is visible from the URL.
    expect(
      screen.getByRole("button", { name: "Ta bort Stockholm" }),
    ).toBeInTheDocument();

    // Add a bransch through the popover (client-side filter, no network).
    await pickBransch(user, "system", "62020 Systemutveckling och programvarutveckling");

    // Edit the name field too.
    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), "Volvo");

    // ONE submit carries all three axes together.
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({
        namn: "Volvo",
        sni: ["62020"],
        kommun: ["0180"],
      }),
    );
    // A name+filter commit never touches the org.nr POST path.
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("shows the unapplied-changes line when the draft diverges from the applied filter", async () => {
    renderBar();
    const user = userEvent.setup();

    expect(
      screen.queryByText("Ändringarna tillämpas när du söker."),
    ).not.toBeInTheDocument();

    await pickBransch(user, "datapro", "62 Dataprogrammering, datakonsultverksamhet");

    expect(
      screen.getByText("Ändringarna tillämpas när du söker."),
    ).toBeInTheDocument();
  });
});

describe("ForetagSokSearchbar — bransch popover (#999)", () => {
  it("filters at ALL THREE SNI levels, not leaves only", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    renderBar();
    const user = userEvent.setup();

    const dialog = await openBransch(user);
    // "verksamhet" occurs in the SECTION name, a DIVISION name and a LEAF name in the fixture. The
    // control this replaced searched all three; the picker used to match leaves only, which is the
    // half of "hard to find" (#999) that no popover shape fixes on its own.
    await user.type(within(dialog).getByLabelText("Sök bransch"), "verksamhet");

    for (const name of [
      "J Informations- och kommunikationsverksamhet", // section
      "62 Dataprogrammering, datakonsultverksamhet", // division
      "62010 Datakonsultverksamhet", // leaf
    ]) {
      expect(within(dialog).getByRole("checkbox", { name })).toBeInTheDocument();
    }
    // Still no network: the filter runs over the reference the page already loaded.
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("ticking a matched DIVISION selects its whole expansion", async () => {
    renderBar();
    const user = userEvent.setup();

    await pickBransch(user, "datapro", "62 Dataprogrammering, datakonsultverksamhet");
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    // Both of division 62's leaves ride the URL — a parent is stored as its leaves, never as itself.
    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: ["62010", "62020"], kommun: [] }),
    );
  });

  it("a partially selected division reads as mixed, so a click cannot silently deselect", async () => {
    // One leaf of division 62 applied — what picking a single leaf and searching produces.
    renderBar({ sni: ["62010"] });
    const user = userEvent.setup();

    const dialog = await openBransch(user);
    await user.type(within(dialog).getByLabelText("Sök bransch"), "datapro");

    expect(
      within(dialog).getByRole("checkbox", {
        name: "62 Dataprogrammering, datakonsultverksamhet",
      }),
    ).toHaveAttribute("aria-checked", "mixed");
  });

  it("is MULTI-select: two branches coexist instead of replacing each other", async () => {
    renderBar();
    const user = userEvent.setup();

    await pickBransch(user, "system", "62020 Systemutveckling och programvarutveckling");
    await pickBransch(user, "webb", "63120 Webbportaler");

    expect(
      screen.getByRole("button", {
        name: "Ta bort Systemutveckling och programvarutveckling",
      }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Ta bort Webbportaler" }),
    ).toBeInTheDocument();
  });

  it("chips decompose upward: a fully selected division reads as ONE division chip", () => {
    renderBar({ sni: ["62010", "62020"] });
    expect(
      screen.getByText("Dataprogrammering, datakonsultverksamhet"),
    ).toBeInTheDocument();
    // Not also its two leaves — the decomposition emits the fewest nodes that describe the set.
    expect(screen.queryByText("Datakonsultverksamhet")).not.toBeInTheDocument();
  });

  it("chips decompose to the SECTION when every leaf under it is selected", () => {
    renderBar({ sni: ["62010", "62020", "63110", "63120"] });
    expect(
      screen.getByText("Informations- och kommunikationsverksamhet"),
    ).toBeInTheDocument();
    expect(
      screen.queryByText("Dataprogrammering, datakonsultverksamhet"),
    ).not.toBeInTheDocument();
  });

  it("removing a chip removes exactly that node's leaves, leaving the rest", async () => {
    renderBar({ sni: ["62010", "62020", "63110"] });
    const user = userEvent.setup();

    await user.click(
      screen.getByRole("button", {
        name: "Ta bort Dataprogrammering, datakonsultverksamhet",
      }),
    );
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: ["63110"], kommun: [] }),
    );
  });

  it("carries the one sentence that explains what a click here does", async () => {
    renderBar();
    const user = userEvent.setup();
    const dialog = await openBransch(user);
    // "Välj bransch" names the control; it does not say that a checkbox on a parent selects its whole
    // subtree, or that several branches can be picked. One click can select 52 codes, and after #999
    // this is the only place on the surface that says so.
    expect(
      within(dialog).getByText(
        "Välj en eller flera branscher. Du kan välja en hel avdelning, en huvudgrupp eller enskilda koder.",
      ),
    ).toBeInTheDocument();
    // And the sentence that paid no rent is gone: a hint under a field already labelled "Sök bransch".
    expect(
      within(dialog).queryByText("Skriv för att smalna av listan över branscher."),
    ).not.toBeInTheDocument();
  });

  it("has the panel's counter region mounted and empty before the first pick", async () => {
    // The 0→1 transition is the one that matters most, and it is exactly the case a region gated on
    // "something is selected" gets wrong: it MOUNTS carrying the new text, which is the form the
    // house has ruled unreliable. So the region has to be there, empty, from the moment the panel is.
    renderBar();
    const user = userEvent.setup();
    const dialog = await openBransch(user);
    const region = dialog.querySelector("span[aria-live='polite']");
    expect(region).not.toBeNull();
    expect(region).toHaveTextContent("");
    // Nothing to clear at zero, so the clear control IS conditional — only the region is not.
    expect(within(dialog).queryByRole("button", { name: "Rensa" })).not.toBeInTheDocument();
  });

  it("the panel and the chip row report the SAME number for the same axis", async () => {
    // One click on the section picks four leaves and decomposes to ONE chip. Counting `selected.size`
    // would put "4 valda branscher" in the panel beside a single chip outside it.
    renderBar();
    const user = userEvent.setup();
    const dialog = await openBransch(user);
    await user.click(
      within(dialog).getByRole("checkbox", {
        name: "Informations- och kommunikationsverksamhet",
      }),
    );

    const counter = within(dialog).getByText("1 vald bransch");
    expect(counter).toBeInTheDocument();
    // Announced, because the number changes without focus moving: one click on a section takes it
    // from nothing to a whole subtree, and the row's own aria-checked does not carry the total.
    expect(counter).toHaveAttribute("aria-live", "polite");
    await user.keyboard("{Escape}");
    expect(
      screen.getByRole("button", {
        name: "Ta bort Informations- och kommunikationsverksamhet",
      }),
    ).toBeInTheDocument();
  });

  it("above the cap the chips collapse to one summary chip that clears the axis", async () => {
    // Ten divisions, one leaf picked in each — a decomposition of ten chips, which is what the cap
    // exists to prevent. Reachable by ticking ten leaves; nothing here is a shape the UI cannot make.
    const wide: CriterionReference = {
      ...REFERENCE,
      sni: [
        {
          code: "C",
          name: "Tillverkning",
          divisions: Array.from({ length: 10 }, (_, i) => ({
            code: `${10 + i}`,
            name: `Huvudgrupp ${10 + i}`,
            leaves: [
              { code: `${10 + i}100`, name: `Detalj ${10 + i}100` },
              { code: `${10 + i}200`, name: `Detalj ${10 + i}200` },
            ],
          })),
        },
      ],
    };
    render(
      <ForetagSokSearchbar
        reference={wide}
        referenceOk
        namn=""
        sni={Array.from({ length: 10 }, (_, i) => `${10 + i}100`)}
        kommun={[]}
      />,
    );
    const user = userEvent.setup();

    expect(screen.getByText("10 valda branscher")).toBeInTheDocument();
    expect(screen.queryByText("Detalj 10100")).not.toBeInTheDocument();

    // The summary REPORTS. It must not carry the same × as the per-branch chips: identical pixels,
    // and one of them drops the entire draft with no undo.
    const summary = screen.getByText("10 valda branscher").closest(".jp-chip")!;
    expect(within(summary as HTMLElement).queryByRole("button")).not.toBeInTheDocument();

    // The bulk removal is a sibling with visible text, at the same size as the row's other control.
    await user.click(screen.getByRole("button", { name: "Ta bort alla branscher" }));
    expect(screen.queryByText("10 valda branscher")).not.toBeInTheDocument();
  });
});

describe("ForetagSokSearchbar — ort", () => {
  it("seeds ort chips from the URL kommun and removes one from the draft", async () => {
    renderBar({ kommun: ["0180", "0181"] });
    const user = userEvent.setup();

    expect(
      screen.getByRole("button", { name: "Ta bort Stockholm" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Ta bort Södertälje" }),
    ).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Ta bort Stockholm" }));

    expect(
      screen.queryByRole("button", { name: "Ta bort Stockholm" }),
    ).not.toBeInTheDocument();
    // Removing a chip edits the draft only — it does not navigate.
    expect(push).not.toHaveBeenCalled();
  });

  it("opens the cascade popover and adds a kommun to the draft", async () => {
    renderBar();
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Välj ort eller län" }));
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Stockholms län" }));
    await user.click(screen.getByRole("checkbox", { name: "Stockholm" }));

    expect(
      screen.getByRole("button", { name: "Ta bort Stockholm" }),
    ).toBeInTheDocument();
  });
});

describe("ForetagSokSearchbar — unified name/org.nr field", () => {
  it("routes a non-org.nr value to the NAME branch: pushes the shareable URL, never fetches", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    renderBar();
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), "Volvo");
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "Volvo", sni: [], kommun: [] }),
    );
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("REFUSES a personnummer-shaped value LOCALLY: never fetches, never navigates (D8(c))", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    renderBar();
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), PNR_SHAPED);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(
      await screen.findByText(/Det ser ut som ett personnummer/i),
    ).toBeInTheDocument();
    // The value left the browser by NO path: not the org.nr POST, not the name-branch URL.
    expect(fetchMock).not.toHaveBeenCalled();
    expect(push).not.toHaveBeenCalled();
  });

  it("routes a 10-digit value to the ORG.NR branch: POSTs, never the URL", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    global.fetch = fetchMock;
    renderBar();
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), VALID_ORGNR);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(await screen.findByText("Volvo AB")).toBeInTheDocument();
    expect(screen.getByText("Göteborg", { exact: false })).toBeInTheDocument();

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/foretag/sok");
    expect(JSON.parse(init.body as string)).toEqual({ organizationNumber: VALID_ORGNR });
    expect(push).not.toHaveBeenCalled();
  });

  it("ignores the bransch/ort draft on an org.nr lookup (org.nr never enters the URL)", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    global.fetch = fetchMock;
    renderBar({ sni: ["62020"], kommun: ["0180"] });
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), VALID_ORGNR);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    await screen.findByText("Volvo AB");
    // Even with an active bransch/ort draft, the org.nr path POSTs only the org.nr and never navigates.
    expect(JSON.parse((fetchMock.mock.calls[0]![1] as RequestInit).body as string)).toEqual({
      organizationNumber: VALID_ORGNR,
    });
    expect(push).not.toHaveBeenCalled();
  });

  it("renders a Bevaka affordance on the org.nr result and follows via the org.nr", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    followActionMock.mockResolvedValue({ success: true, companyWatchId: "cw-new" });
    renderBar();
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), VALID_ORGNR);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    const bevaka = await screen.findByRole("button", { name: "Bevaka Volvo AB" });
    await user.click(bevaka);

    expect(followActionMock).toHaveBeenCalledWith(VALID_ORGNR);
    expect(await screen.findByText("Bevakar")).toBeInTheDocument();
  });

  it("shows the not-found state when the register has no such org.nr", async () => {
    global.fetch = vi.fn().mockResolvedValue(orgNrResponse(null));
    renderBar();
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), VALID_ORGNR);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(
      await screen.findByText("Inget företag med det numret"),
    ).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });

  it("surfaces a concrete retry time on 429 (Retry-After → seconds in copy)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(null, { status: 429, headers: { "Retry-After": "30" } }),
    );
    renderBar();
    const user = userEvent.setup();

    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      VALID_ORGNR,
    );
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(await screen.findByText(/Vänta 30 sekunder/i)).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });

  it("renders the technical-error state on a non-ok backend response", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 502 }));
    renderBar();
    const user = userEvent.setup();

    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      VALID_ORGNR,
    );
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /Sökningen kunde inte genomföras/i,
    );
    expect(push).not.toHaveBeenCalled();
  });
});

/**
 * ADR 0087 D8(c) — the CALL-SITE pin, not only the rule.
 *
 * `parseNamn`'s gate is unit-tested next door, but a rule test cannot see what the FORM hands the
 * browser. This suite serialises the form exactly as a native GET would and asserts the URL it
 * produces — which is the instrument that measured the defect in the first place: on HEAD before the
 * fix, typing `1010101010` and reading `new FormData(form)` produced
 * `/foretag/sok?namn=1010101010`. Without this, a later edit could restore `name="namn"` on the
 * visible input and every other test in this file would stay green.
 */
describe("ForetagSokSearchbar — what a NATIVE GET would carry (D8(c) call-site pin)", () => {
  function nativeGetHref(input: HTMLElement): string {
    const form = input.closest("form");
    if (form === null) throw new Error("the search input is not inside a form");
    const params = new URLSearchParams();
    for (const [key, value] of new FormData(form).entries()) {
      params.append(key, String(value));
    }
    return `${form.getAttribute("action")}?${params.toString()}`;
  }

  it("never carries the TYPED value — a ten-digit draft cannot reach ?namn=", async () => {
    renderBar();
    const user = userEvent.setup();
    const input = screen.getByLabelText("Företagsnamn eller organisationsnummer");

    await user.type(input, PNR_SHAPED);

    const href = nativeGetHref(input);
    expect(href).not.toContain(PNR_SHAPED);
    expect(href).not.toContain("namn=");
  });

  it("does not carry a typed ORDINARY name either — only what is already applied", async () => {
    // The rule is not "gate the pnr class in the form"; it is "the form never carries the draft".
    // A draft-carrying form would be one predicate away from leaking again.
    renderBar();
    const user = userEvent.setup();
    const input = screen.getByLabelText("Företagsnamn eller organisationsnummer");

    await user.type(input, "Volvo");

    expect(nativeGetHref(input)).not.toContain("Volvo");
  });

  it("carries the APPLIED name (which has passed the server gate) via a hidden input", () => {
    renderBar({ namn: "Volvo", kommun: ["0180"] });
    const input = screen.getByLabelText("Företagsnamn eller organisationsnummer");

    const href = nativeGetHref(input);
    expect(href).toContain("namn=Volvo");
    expect(href).toContain("kommun=0180");
  });

  it("strips the name attribute from the visible input once hydrated", () => {
    renderBar({ namn: "Volvo" });
    // The visible field still SHOWS the applied name; it just does not submit it.
    const input = screen.getByLabelText("Företagsnamn eller organisationsnummer");
    expect(input).toHaveValue("Volvo");
    expect(input).not.toHaveAttribute("name");
  });

  /**
   * The OTHER side of the hydration split, which `render()` can never reach: every RTL render is a
   * client render, so `hydrated` is `true` in all of the tests above. Server-render the component
   * instead, which is what `getServerSnapshot` (false) actually drives.
   *
   * Without this, replacing `name={hydrated ? undefined : "namn"}` with a bare `name={undefined}`
   * leaves the whole suite green — and silently kills no-JS name search, because the field would
   * submit nothing and every no-JS search would return the entire register.
   */
  it("KEEPS the name attribute before hydration, so a no-JS search still works", () => {
    // The fixture carries an APPLIED name deliberately. With `namn=""` the second assertion would be
    // vacuous — the hidden input is gated on `namn.length > 0`, so it is absent whatever `hydrated`
    // says, and dropping the `hydrated &&` guard would leave the test green.
    const html = renderToString(
      <NextIntlClientProvider locale="sv" messages={{ pages: svPages }}>
        <ForetagSokSearchbar
          reference={REFERENCE}
          referenceOk
          namn="Volvo"
          sni={[]}
          kommun={[]}
        />
      </NextIntlClientProvider>,
    );
    // Exactly ONE `name="namn"` pre-hydration — the visible input. Counting rather than matching a
    // literal attribute order: two would mean the visible input and the hidden applied-name input
    // both submit, which is the state the hydration split exists to make impossible.
    expect(html.match(/name="namn"/g) ?? []).toHaveLength(1);
    expect(html).toContain('value="Volvo"');
  });
});

describe("ForetagSokSearchbar — degraded reference", () => {
  it("disables the bransch field with a civil notice; the name search still works", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    render(
      <ForetagSokSearchbar
        reference={{ sniVersion: "", kommunVersion: "", sni: [], lan: [] }}
        referenceOk={false}
        namn=""
        sni={[]}
        kommun={[]}
      />,
    );
    const user = userEvent.setup();

    expect(screen.getByRole("button", { name: "Välj bransch" })).toBeDisabled();
    expect(
      screen.getByText(/Branschlistan kunde inte laddas just nu/i),
    ).toBeInTheDocument();

    // The reference-free name field keeps working.
    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), "Acme");
    await user.click(screen.getByRole("button", { name: "Sök företag" }));
    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "Acme", sni: [], kommun: [] }),
    );
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("still shows an APPLIED bransch filter it cannot name, and lets you remove it", async () => {
    // Producible by a path in src/: the reference endpoint is down (page.tsx falls back to
    // EMPTY_REFERENCE) while the visitor arrives on a shared link carrying ?sni=. page.tsx passes NO
    // allowlist to normalizeCodes in that branch, so the axis survives and IS applied to the results
    // below — but an empty tree means no chip can be named for it. Before this branch the row rendered
    // nothing: an active, invisible, unremovable filter.
    render(
      <ForetagSokSearchbar
        reference={{ sniVersion: "", kommunVersion: "", sni: [], lan: [] }}
        referenceOk={false}
        namn=""
        sni={["62010", "62020"]}
        kommun={[]}
      />,
    );
    const user = userEvent.setup();

    // Counted in the unit it can honestly report: codes, not named branches.
    expect(screen.getByText("2 branschkoder")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Ta bort alla branscher" }));
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: [], kommun: [] }),
    );
  });

  it("keeps the org.nr answer OUT of the form's width cap, and the clear control on the rail", () => {
    // #1090, both halves, pinned at the only level jsdom can see: structure and classes. The widths
    // themselves (672 vs 1136) and the text's x-position are rendered measurements in the PR body —
    // jsdom has no layout. What it CAN pin is that the org.nr <section> is not inside the capped
    // wrapper, and that the clear control carries the modifier that cancels the button's padding.
    const { container } = render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={[]}
      />,
    );
    const capped = container.querySelector(".max-w-2xl");
    expect(capped).not.toBeNull();
    const answer = container.querySelector("section[aria-live='polite']");
    expect(answer).not.toBeNull();
    expect(capped!.contains(answer!)).toBe(false);

    expect(capped!.contains(container.querySelector("form"))).toBe(true);

    // The class alone guarantees nothing: `.jp-btn--flush` is CSS-scoped to `:first-child`, so a
    // wrapper, an inserted sibling, or an unconditionally rendered chip list would silently kill the
    // offset with every gate green (guard:css reads the stylesheet, jsdom has no cascade, eslint sees
    // a valid string). The POSITION is structural, which is exactly what jsdom can see — so it is
    // asserted rather than excused.
    const clear = screen.getByRole("button", { name: "Rensa sökningen" });
    expect(clear).toHaveClass("jp-btn--flush");
    expect(clear.parentElement!.firstElementChild).toBe(clear);
  });

  it("leaves the flush offset inert when chips precede the clear control", () => {
    // The other arm, and the reason the modifier is CSS-scoped rather than always-on: here the
    // negative start margin would eat 18px of a 12px gap and lap the last chip.
    render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn=""
        sni={["62010"]}
        kommun={[]}
      />,
    );
    const clear = screen.getByRole("button", { name: "Rensa sökningen" });
    expect(clear.parentElement!.firstElementChild).not.toBe(clear);
  });

  it("never renders an empty chip list", () => {
    render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={[]}
      />,
    );
    // A pure name search shows the clear control but no chips. A <ul> with no <li> is announced as
    // "list, 0 items" — a container for nothing.
    expect(screen.getByRole("button", { name: "Rensa sökningen" })).toBeInTheDocument();
    expect(screen.queryByRole("list")).not.toBeInTheDocument();
  });
});

/**
 * The live-review fixes (Klas, 2026-07-25). Each of these was a measured complaint about the shipped
 * S2 surface, not a hypothetical — the design framing is
 * `docs/reviews/2026-07-25-foretag-sok-followup-design.md`.
 */
describe("ForetagSokSearchbar — the live-review fixes", () => {
  it("CLEARS the whole search and NAVIGATES, not just the draft filter (finding 6)", async () => {
    renderBar({ namn: "Volvo", kommun: ["0180"] });
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Rensa sökningen" }));

    // It navigates: the old version nulled two draft fields and left the applied URL filter in
    // place, so the results below kept answering a search the controls no longer showed.
    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: [], kommun: [] }),
    );
    // ...and the name field is cleared too, which it never was.
    expect(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
    ).toHaveValue("");
  });

  it("offers the clear control for a PURE NAME search, which had no clear path at all", () => {
    // The old gate was `hasFilter` (bransch or ort), so the most common search — a name — could not
    // be cleared. That is the whole of finding 6.
    renderBar({ namn: "Volvo" });
    expect(
      screen.getByRole("button", { name: "Rensa sökningen" }),
    ).toBeInTheDocument();
  });

  it("hides the clear control when there is genuinely nothing to clear", () => {
    renderBar();
    expect(
      screen.queryByRole("button", { name: "Rensa sökningen" }),
    ).not.toBeInTheDocument();
  });

  it("stays hidden while the user TYPES — the gate reads applied state, never the draft", async () => {
    // Reading the draft made the control appear on the first keystroke and shove the results 64px
    // down mid-typing (measured). Every other clear test clicks without typing, so without this the
    // gate can be widened back to `value` with the whole suite green.
    renderBar();
    const user = userEvent.setup();

    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      "Volvo",
    );

    expect(
      screen.queryByRole("button", { name: "Rensa sökningen" }),
    ).not.toBeInTheDocument();
  });

  it("renders the org.nr answer through the shared register table (finding 5)", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    renderBar();
    const user = userEvent.setup();

    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      VALID_ORGNR,
    );
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    // A real table with the register's own columns — not a hand-rolled card that can drift from it.
    const table = await screen.findByRole("table", {
      name: "Företag som matchar organisationsnumret",
    });
    expect(table).toBeInTheDocument();
    expect(within(table).getByText("Volvo AB")).toBeInTheDocument();
    // The seat renders WITHOUT the SCB code (finding 7) — it used to read "Göteborg (1480)".
    expect(within(table).queryByText(/\(1480\)/)).not.toBeInTheDocument();
  });

  it("groups the narrowing controls under their own labelled section (finding 4)", () => {
    renderBar();
    // The two interaction models differ — the name SUBMITS, these narrow — so the difference is
    // drawn as a group rather than explained in more hint prose (which finding 9 asked to reduce).
    expect(screen.getByRole("group", { name: "Avgränsa" })).toBeInTheDocument();
  });

  it("drops both filter hints, whose triggers already say the same thing (finding 9)", () => {
    renderBar();
    expect(
      screen.queryByText("Välj ett eller flera län eller kommuner."),
    ).not.toBeInTheDocument();
    // The bransch hint described a typeahead that no longer exists; "Välj bransch" names itself.
    expect(
      screen.queryByText("Skriv och välj en bransch."),
    ).not.toBeInTheDocument();
  });

  it("names the bransch trigger without a <label htmlFor> (WCAG 2.5.3 label-in-name)", () => {
    renderBar();
    // A label pointed at a button becomes its accessible name and overrides the visible text. The
    // trigger must therefore be findable by what it SAYS, and not by the field heading beside it.
    expect(
      screen.getByRole("button", { name: "Välj bransch" }),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText("Bransch")).not.toBeInTheDocument();
  });
});

/**
 * The island is rendered without a `key`, so it never remounts, and all three draft pieces are
 * `useState` initialisers that run once. Without a re-seed, Back after a search leaves the field and
 * chips showing what you just left while the URL and results show something else. "Rensa sökningen"
 * makes that reachable in one click.
 */
describe("ForetagSokSearchbar — the draft re-seeds when the applied URL changes", () => {
  it("re-seeds the field and chips when the applied props change (Back)", () => {
    const { rerender } = render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={["62020"]}
        kommun={["0180"]}
      />,
    );
    expect(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
    ).toHaveValue("Volvo");
    expect(screen.getByRole("button", { name: "Ta bort Stockholm" })).toBeInTheDocument();

    // Same component instance, new applied URL — what Back does.
    rerender(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Saab"
        sni={[]}
        kommun={[]}
      />,
    );

    expect(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
    ).toHaveValue("Saab");
    expect(
      screen.queryByRole("button", { name: "Ta bort Stockholm" }),
    ).not.toBeInTheDocument();
  });

  it("re-seeds the BRANSCH chip too, not only the name and orter", () => {
    // Without this, deleting `setBranch(seedBranch(...))` from the re-seed leaves the suite green:
    // the test above only asserts the ort chip.
    const { rerender } = render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn=""
        sni={["62010", "62020"]}
        kommun={[]}
      />,
    );
    expect(
      screen.getByText("Dataprogrammering, datakonsultverksamhet"),
    ).toBeInTheDocument();

    rerender(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn=""
        sni={["63110", "63120"]}
        kommun={[]}
      />,
    );

    expect(screen.getByText("Informationstjänster")).toBeInTheDocument();
    expect(
      screen.queryByText("Dataprogrammering, datakonsultverksamhet"),
    ).not.toBeInTheDocument();
  });

  it("re-seeds when ONLY an axis changes — the signature is not just the name", () => {
    // Truncating the signature to `${namn}` leaves every other re-seed test green, because none of
    // them holds the name constant while an axis moves. This one does.
    const { rerender } = render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={["0180"]}
      />,
    );
    expect(screen.getByRole("button", { name: "Ta bort Stockholm" })).toBeInTheDocument();

    rerender(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={["0181"]}
      />,
    );

    expect(screen.getByRole("button", { name: "Ta bort Södertälje" })).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Ta bort Stockholm" }),
    ).not.toBeInTheDocument();
  });

  it("does NOT clobber what the user is typing when the applied props are unchanged", async () => {
    const { rerender } = render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={[]}
      />,
    );
    const user = userEvent.setup();
    const input = screen.getByLabelText("Företagsnamn eller organisationsnummer");
    await user.clear(input);
    await user.type(input, "Scania");

    // A re-render that does not change the applied search — the draft must survive it, or the
    // re-seed would fight the user on every keystroke-adjacent render.
    rerender(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={[]}
      />,
    );

    expect(input).toHaveValue("Scania");
  });
});

/**
 * The org.nr answer is client-only state and is deliberately NOT part of the applied signature, so
 * the re-seed has to drop it explicitly. Reachable: search, search, look up an org.nr, press Back.
 */
describe("ForetagSokSearchbar — a standing org.nr answer does not survive a re-seed", () => {
  it("clears the org.nr result when the applied URL changes", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    const { rerender } = render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={[]}
      />,
    );
    const user = userEvent.setup();

    await user.clear(screen.getByLabelText("Företagsnamn eller organisationsnummer"));
    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      VALID_ORGNR,
    );
    await user.click(screen.getByRole("button", { name: "Sök företag" }));
    expect(await screen.findByText("Volvo AB")).toBeInTheDocument();

    // Back: the applied URL moves, and the stale company row must not sit above it.
    rerender(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Saab"
        sni={[]}
        kommun={[]}
      />,
    );

    expect(screen.queryByText("Volvo AB")).not.toBeInTheDocument();
  });
});
