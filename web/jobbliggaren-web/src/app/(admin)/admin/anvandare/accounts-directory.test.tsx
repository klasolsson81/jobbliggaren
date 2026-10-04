import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AccountsListing } from "@/lib/admin/account-directory";
import { toAccountsPage, type AccountDetailsDto, type AccountSearchResponse } from "@/lib/dto/admin-accounts";
import { AccountsDirectory } from "./accounts-directory";

const A = {
  id: "00000000-0000-4000-8000-000000000001",
  email: "konto.a@example.test",
  role: "User",
  status: "Active",
  emailConfirmed: true,
  registeredAt: "2026-09-28T12:02:00Z",
  deletionEarliest: null,
  applicationCount: 4,
} as const;

const B = {
  id: "00000000-0000-4000-8000-000000000002",
  email: "konto.b@example.test",
  role: "User",
  status: "ProfileMissing",
  emailConfirmed: true,
  registeredAt: null,
  deletionEarliest: null,
  applicationCount: null,
} as const;

function answer(items: AccountSearchResponse["accounts"]["items"]): AccountSearchResponse {
  return {
    accounts: { items, totalCount: items.length, page: 1, pageSize: 25, totalPages: 1 },
    counts: { total: 2, active: 1, pendingDeletion: 0, profileMissing: 1 },
  };
}

const FIRST: AccountsListing = { kind: "loaded", page: toAccountsPage(answer([A, B])) };

const DETAIL: AccountDetailsDto = { ...A, resumeCount: 2, savedSearchCount: 3 };

const fetchMock = vi.fn<(path: string, init: RequestInit) => Promise<Response>>();

function json(body: unknown, status = 200, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });
}

function call(index: number) {
  const [path, init] = fetchMock.mock.calls[index] ?? [];
  return { path, init, body: JSON.parse(String(init?.body)) as Record<string, unknown> };
}

function shownAddresses() {
  return within(screen.getByRole("table", { name: "Konton" }))
    .getAllByRole("row")
    .slice(1)
    .map((row) => within(row).getAllByRole("cell")[0]?.textContent);
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("AccountsDirectory (#1974, ADR 0151)", () => {
  it("asks nothing on its own: the server answered the first page", async () => {
    render(<AccountsDirectory initial={FIRST} />);

    await new Promise((resolve) => setTimeout(resolve, 400));
    expect(fetchMock).not.toHaveBeenCalled();
    expect(shownAddresses()).toEqual(["konto.a@example.test", "konto.b@example.test"]);
  });

  it("searches once typing pauses, with the term in the request body and never in its URL", async () => {
    fetchMock.mockResolvedValue(json(answer([B])));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.type(screen.getByRole("searchbox", { name: "Sök på e-postadress" }), " konto.b ");
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));

    const { path, init, body } = call(0);
    expect(path).toBe("/api/admin/konton");
    expect(init?.method).toBe("POST");
    expect(init?.cache).toBe("no-store");
    expect(body).toEqual({ address: "konto.b", sort: "RegisteredNewest", page: 1, pageSize: 25 });
    await waitFor(() => expect(shownAddresses()).toEqual(["konto.b@example.test"]));
    expect(screen.getByRole("status")).toHaveTextContent("1 av 2 konton");
  });

  it("filters and sorts by the backend's names, each from the first page", async () => {
    fetchMock.mockResolvedValue(json(answer([A, B])));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.click(screen.getByRole("radio", { name: "Ofullständiga (1)" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(call(0).body).toMatchObject({ status: "ProfileMissing", sort: "RegisteredNewest", page: 1 });

    await userEvent.click(screen.getByRole("button", { name: "Konto" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(call(1).body).toMatchObject({ status: "ProfileMissing", sort: "AddressAscending", page: 1 });
  });

  it("keeps its rows while a newer read is on its way, and aborts a read the next one replaces", async () => {
    fetchMock.mockImplementationOnce(() => new Promise<Response>(() => {}));
    fetchMock.mockResolvedValue(json(answer([B])));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.click(screen.getByRole("radio", { name: "Aktiva (1)" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(screen.getByRole("table", { name: "Konton" })).toHaveAttribute("aria-busy", "true");
    expect(shownAddresses()).toEqual(["konto.a@example.test", "konto.b@example.test"]);

    await userEvent.click(screen.getByRole("radio", { name: "Ofullständiga (1)" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(call(0).init?.signal?.aborted).toBe(true);
    await waitFor(() => expect(shownAddresses()).toEqual(["konto.b@example.test"]));
    expect(screen.getByRole("table", { name: "Konton" })).not.toHaveAttribute("aria-busy");
  });

  it("words a rate-limited search with its wait, in place of the rows and the counts", async () => {
    fetchMock.mockResolvedValue(json({ error: "rateLimited" }, 429, { "Retry-After": "6" }));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.click(screen.getByRole("radio", { name: "Aktiva (1)" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("För många förfrågningar. Försök igen om 6 sekunder.");
    expect(screen.getByRole("radiogroup", { name: "Visa konton" }).textContent ?? "").not.toMatch(/\d/);
  });

  it("opens an account at once, reads its details by a body, and shows them when they arrive", async () => {
    let arrive: (response: Response) => void = () => {};
    fetchMock.mockImplementationOnce(() => new Promise<Response>((resolve) => (arrive = resolve)));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.click(screen.getByRole("button", { name: "konto.a@example.test" }));
    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    expect(within(dialog).getByRole("status")).toHaveTextContent("Hämtar kontots uppgifter…");
    const { path, body } = call(0);
    expect(path).toBe("/api/admin/konton/detalj");
    expect(body).toEqual({ id: A.id });

    arrive(json(DETAIL));
    await waitFor(() => expect(within(dialog).getByText("CV:n").nextElementSibling).toHaveTextContent("2"));
    expect(within(dialog).getByText("Sparade sökningar").nextElementSibling).toHaveTextContent("3");
  });

  it("says an account that no longer exists is gone, and reads the list again", async () => {
    fetchMock.mockResolvedValueOnce(json({ error: "notFound" }, 404));
    fetchMock.mockResolvedValue(json(answer([B])));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.click(screen.getByRole("button", { name: "konto.a@example.test" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Kontot finns inte längre.");
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(call(1).path).toBe("/api/admin/konton");
  });
});
