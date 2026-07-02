import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyLookup } from "./company-lookup";

// Server action mocked at the module boundary (the island calls it inside a transition).
const followMock = vi.fn();
vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyAction: (orgNr: string) => followMock(orgNr),
}));

const fetchMock = vi.fn();

function jsonResponse(body: unknown, status = 200, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json", ...headers },
  });
}

function foundBody(overrides: Record<string, unknown> = {}) {
  return {
    status: "found",
    organizationNumber: "5560125790",
    isProtectedIdentity: false,
    companyName: "Volvo Aktiebolag",
    activeAdCount: 0,
    matchingAdCount: null,
    companyWatchId: null,
    ...overrides,
  };
}

beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
  fetchMock.mockReset();
  followMock.mockReset();
});

async function submitLookup(value: string) {
  const user = userEvent.setup();
  render(<CompanyLookup />);
  await user.type(screen.getByLabelText("Organisationsnummer"), value);
  await user.click(screen.getByRole("button", { name: "Sök" }));
  return user;
}

describe("CompanyLookup (#454 — /foretag-uppslagskortet)", () => {
  it("submit är disablad tills värdet normaliserar till 10 siffror", async () => {
    const user = userEvent.setup();
    render(<CompanyLookup />);
    const button = screen.getByRole("button", { name: "Sök" });
    expect(button).toBeDisabled();
    await user.type(screen.getByLabelText("Organisationsnummer"), "556012-5790");
    expect(button).toBeEnabled();
  });

  it("pnr-shaped värde ⇒ refuse-kort LOKALT — ingen transmission, ingen bevaka-affordance", async () => {
    // ADR 0088 D4 (Posture A) + CTO-sub-bind: rent stopp; värdet lämnar aldrig webbläsaren.
    await submitLookup("190101-2384"); // 3:e siffran 0 ⇒ pnr-shaped

    expect(await screen.findByText("Numret kan inte slås upp")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Bevaka" })).toBeNull();
  });

  it("found ⇒ namn + counts + länkar med employer-param + bevaka-knapp", async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse(foundBody()));

    await submitLookup("5560125790");

    expect(await screen.findByText("Volvo Aktiebolag")).toBeInTheDocument();
    // 0-annons-berättelsen + not-assessed-nudgen (matchingAdCount null).
    expect(screen.getByText("Inga aktiva annonser just nu")).toBeInTheDocument();
    const seeAds = screen.getByRole("link", { name: "Se annonser" });
    expect(seeAds).toHaveAttribute("href", "/jobb?employer=5560125790");
    const seeMatching = screen.getByRole("link", { name: "Se matchande annonser" });
    expect(seeMatching).toHaveAttribute(
      "href",
      "/jobb?employer=5560125790&baraMatchade=on",
    );
    expect(screen.getByRole("button", { name: "Bevaka" })).toBeInTheDocument();
    // Bodyn POSTades med normaliserat org.nr — aldrig i URL:en (D8(c)).
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/foretag/lookup");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body as string)).toEqual({ organizationNumber: "5560125790" });
  });

  it("bevaka anropar followCompanyAction och visar Bevakas vid success", async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse(foundBody()));
    followMock.mockResolvedValueOnce({ success: true, companyWatchId: "watch-1" });

    const user = await submitLookup("5560125790");
    await user.click(await screen.findByRole("button", { name: "Bevaka" }));

    await waitFor(() => expect(followMock).toHaveBeenCalledWith("5560125790"));
    expect(await screen.findByText("Bevakas")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Bevaka" })).toBeNull();
  });

  it("redan bevakad (companyWatchId satt) ⇒ Bevakas utan knapp", async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse(foundBody({ companyWatchId: "watch-9" })),
    );

    await submitLookup("5560125790");

    expect(await screen.findByText("Bevakas")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Bevaka" })).toBeNull();
  });

  it("notFound ⇒ civic tomstate utan skuldbeläggning", async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse(foundBody({ status: "notFound", organizationNumber: null, companyName: null })),
    );

    await submitLookup("5599999999");

    expect(await screen.findByText("Inget företag hittades")).toBeInTheDocument();
  });

  it("unavailable ⇒ civic degraderings-kort (aldrig ett tekniskt fel)", async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse(foundBody({ status: "unavailable", organizationNumber: null, companyName: null })),
    );

    await submitLookup("5560125790");

    expect(
      await screen.findByText("Uppgifter kan inte hämtas just nu"),
    ).toBeInTheDocument();
  });

  it("429 ⇒ rateLimited-kort med konkret retry-tid ur Retry-After", async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ error: "rateLimited" }, 429, { "Retry-After": "42" }),
    );

    await submitLookup("5560125790");

    expect(await screen.findByText("För många uppslag")).toBeInTheDocument();
    expect(screen.getByText("Vänta 42 sekunder och försök igen.")).toBeInTheDocument();
  });
});
