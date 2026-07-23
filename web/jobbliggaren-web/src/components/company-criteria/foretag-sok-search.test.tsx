import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ForetagSokSearch } from "./foretag-sok-search";
import { buildForetagSokHref } from "@/lib/company-search/search-params";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

const followActionMock = vi.fn();
const unfollowActionMock = vi.fn();
vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyAction: (...args: unknown[]) => followActionMock(...args),
  unfollowCompanyAction: (...args: unknown[]) => unfollowActionMock(...args),
}));

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

describe("ForetagSokSearch (unified name-or-org.nr field)", () => {
  it("routes a non-org.nr value to the NAME branch: pushes the shareable URL, never fetches", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    render(<ForetagSokSearch namn="" sni={[]} kommun={[]} />);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök företag"), "Volvo");
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "Volvo", sni: [], kommun: [] }),
    );
    // org.nr branch never ran → the client POST was not made.
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("carries the active SNI/kommun through a name search so it never erases a filter", async () => {
    global.fetch = vi.fn();
    render(<ForetagSokSearch namn="" sni={["62010"]} kommun={["0180"]} />);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök företag"), "Acme");
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "Acme", sni: ["62010"], kommun: ["0180"] }),
    );
  });

  it("REFUSES a personnummer-shaped value LOCALLY: never fetches, never navigates (D8(c) security invariant)", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    render(<ForetagSokSearch namn="" sni={[]} kommun={[]} />);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök företag"), PNR_SHAPED);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    // The refuse state renders locally...
    expect(
      await screen.findByText(/Det ser ut som ett personnummer/i),
    ).toBeInTheDocument();
    // ...and the value left the browser by NO path: not the org.nr POST, not the name-branch URL.
    expect(fetchMock).not.toHaveBeenCalled();
    expect(push).not.toHaveBeenCalled();
  });

  it("routes a 10-digit value to the ORG.NR branch: POSTs to /api/foretag/sok, never the URL", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    global.fetch = fetchMock;
    render(<ForetagSokSearch namn="" sni={[]} kommun={[]} />);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök företag"), VALID_ORGNR);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(await screen.findByText("Volvo AB")).toBeInTheDocument();
    expect(screen.getByText("Göteborg", { exact: false })).toBeInTheDocument();

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/foretag/sok");
    expect(JSON.parse(init.body as string)).toEqual({ organizationNumber: VALID_ORGNR });
    // The org.nr term is a client POST only — it never enters the URL.
    expect(push).not.toHaveBeenCalled();
  });

  it("normalises a hyphenated org.nr to the org.nr branch", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    global.fetch = fetchMock;
    render(<ForetagSokSearch namn="" sni={[]} kommun={[]} />);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök företag"), "556012-5790");
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    await screen.findByText("Volvo AB");
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(JSON.parse(init.body as string)).toEqual({ organizationNumber: "5560125790" });
    expect(push).not.toHaveBeenCalled();
  });

  it("renders a Bevaka affordance on the org.nr result and follows via the org.nr (caveat: follow-via-org.nr)", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    followActionMock.mockResolvedValue({ success: true, companyWatchId: "cw-new" });
    render(<ForetagSokSearch namn="" sni={[]} kommun={[]} />);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök företag"), VALID_ORGNR);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    const bevaka = await screen.findByRole("button", { name: "Bevaka Volvo AB" });
    await user.click(bevaka);

    expect(followActionMock).toHaveBeenCalledWith(VALID_ORGNR);
    expect(await screen.findByText("Bevakar")).toBeInTheDocument();
  });

  it("shows 'Bevakar' immediately when the org.nr result is already followed", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: "cw-1" }));
    render(<ForetagSokSearch namn="" sni={[]} kommun={[]} />);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök företag"), VALID_ORGNR);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(await screen.findByRole("button", { name: "Bevakar Volvo AB" })).toBeInTheDocument();
  });

  it("shows the not-found state when the register has no such org.nr", async () => {
    global.fetch = vi.fn().mockResolvedValue(orgNrResponse(null));
    render(<ForetagSokSearch namn="" sni={[]} kommun={[]} />);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök företag"), VALID_ORGNR);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(await screen.findByText("Inget företag med det numret")).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });

  it("surfaces a concrete retry time on 429", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(null, { status: 429, headers: { "Retry-After": "30" } }),
    );
    render(<ForetagSokSearch namn="" sni={[]} kommun={[]} />);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök företag"), VALID_ORGNR);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(await screen.findByText(/Vänta 30 sekunder/i)).toBeInTheDocument();
  });
});
