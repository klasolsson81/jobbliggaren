import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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

    // Add a bransch via the client typeahead.
    await user.type(screen.getByLabelText("Bransch"), "system");
    await user.click(
      screen.getByRole("option", {
        name: "Systemutveckling och programvarutveckling",
      }),
    );

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

    await user.type(screen.getByLabelText("Bransch"), "datapro");
    await user.click(
      screen.getByRole("option", {
        name: "Dataprogrammering, datakonsultverksamhet",
      }),
    );

    expect(
      screen.getByText("Ändringarna tillämpas när du söker."),
    ).toBeInTheDocument();
  });
});

describe("ForetagSokSearchbar — bransch typeahead", () => {
  it("filters the reference CLIENT-SIDE (no fetch) and turns a pick into a chip", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    renderBar();
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Bransch"), "datapro");
    // Division-level name is a searchable option (CTO granularity), matched client-side.
    await user.click(
      screen.getByRole("option", {
        name: "Dataprogrammering, datakonsultverksamhet",
      }),
    );

    // The pick becomes a removable chip; no network was hit for suggestions.
    expect(
      screen.getByRole("button", { name: "Ta bort bransch" }),
    ).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("supports keyboard selection (ArrowDown + Enter)", async () => {
    renderBar();
    const user = userEvent.setup();

    const input = screen.getByLabelText("Bransch");
    await user.type(input, "system");
    await user.keyboard("{ArrowDown}{Enter}");

    // The single "system…" match was picked → its chip is present.
    expect(
      screen.getByRole("button", { name: "Ta bort bransch" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Systemutveckling och programvarutveckling"),
    ).toBeInTheDocument();
  });

  it("a new pick REPLACES the single existing bransch chip", async () => {
    renderBar();
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Bransch"), "system");
    await user.click(
      screen.getByRole("option", {
        name: "Systemutveckling och programvarutveckling",
      }),
    );
    await user.type(screen.getByLabelText("Bransch"), "webb");
    await user.click(screen.getByRole("option", { name: "Webbportaler" }));

    expect(screen.getByText("Webbportaler")).toBeInTheDocument();
    expect(
      screen.queryByText("Systemutveckling och programvarutveckling"),
    ).not.toBeInTheDocument();
  });

  it("seeds a clean bransch chip from the URL sni (division-level match)", () => {
    renderBar({ sni: ["62010", "62020"] });
    expect(
      screen.getByText("Dataprogrammering, datakonsultverksamhet"),
    ).toBeInTheDocument();
  });

  it("seeds a generic 'Vald bransch' chip when sni matches no single option", () => {
    renderBar({ sni: ["99999"] });
    expect(screen.getByText("Vald bransch")).toBeInTheDocument();
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

    expect(screen.getByLabelText("Bransch")).toBeDisabled();
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
});
