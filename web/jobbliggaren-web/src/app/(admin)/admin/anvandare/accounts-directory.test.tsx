import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AccountsListing } from "@/lib/admin/account-directory";
import { toAccountsPage, type AccountDetailsDto, type AccountSearchResponse } from "@/lib/dto/admin-accounts";
import { AccountsDirectory } from "./accounts-directory";

type Item = AccountSearchResponse["accounts"]["items"][number];

const A: Item = {
  id: "00000000-0000-4000-8000-000000000001",
  email: "konto.a@example.test",
  role: "User",
  status: "Active",
  emailConfirmed: true,
  registeredAt: "2026-09-28T12:02:00Z",
  deletionEarliest: null,
  applicationCount: 4,
};

const B: Item = {
  id: "00000000-0000-4000-8000-000000000002",
  email: "konto.b@example.test",
  role: "User",
  status: "ProfileMissing",
  emailConfirmed: true,
  registeredAt: null,
  deletionEarliest: null,
  applicationCount: null,
};

const C: Item = { ...B, id: "00000000-0000-4000-8000-000000000003", email: "konto.c@example.test" };

/** The counts cover every account the term matches, whatever the filter (Counts_follow_the_term_but_not_the_status_filter). */
function countsOf(matched: ReadonlyArray<Item>): AccountSearchResponse["counts"] {
  const count = (status: Item["status"]) => matched.filter((item) => item.status === status).length;
  return {
    total: matched.length,
    active: count("Active"),
    pendingDeletion: count("PendingDeletion"),
    profileMissing: count("ProfileMissing"),
  };
}

function answer(items: ReadonlyArray<Item>, matched: ReadonlyArray<Item> = items): AccountSearchResponse {
  return {
    accounts: { items: [...items], totalCount: items.length, page: 1, pageSize: 25, totalPages: 1 },
    counts: countsOf(matched),
  };
}

const FIRST: AccountsListing = { kind: "loaded", page: toAccountsPage(answer([A, B, C])) };

const DETAIL: AccountDetailsDto = { ...A, resumeCount: 2, savedSearchCount: 3 };

const MANY: ReadonlyArray<Item> = Array.from({ length: 60 }, (_, index) => ({
  ...(index % 2 === 0 ? A : B),
  id: `00000000-0000-4000-8000-${String(index + 100).padStart(12, "0")}`,
  email: `konto.${String(index).padStart(2, "0")}@example.test`,
}));

/** The directory as the backend answers a search: the term and the filter choose the rows, the term the counts. */
function directory(body: Readonly<Record<string, unknown>>): AccountSearchResponse {
  const term = typeof body.address === "string" ? body.address : "";
  const matched = MANY.filter((item) => item.email?.includes(term));
  const shown = matched.filter((item) => body.status === undefined || item.status === body.status);
  const page = Number(body.page);
  const pageSize = Number(body.pageSize);
  return {
    accounts: {
      items: shown.slice((page - 1) * pageSize, page * pageSize),
      totalCount: shown.length,
      page,
      pageSize,
      totalPages: Math.ceil(shown.length / pageSize),
    },
    counts: countsOf(matched),
  };
}

const fetchMock = vi.fn<(path: string, init: RequestInit) => Promise<Response>>();

function json(body: unknown, status = 200, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });
}

function call(index: number) {
  const [path, init] = fetchMock.mock.calls[index] ?? [];
  return { path, init, body: JSON.parse(String(init?.body)) as Record<string, unknown> };
}

/** The rows' addresses; `hidden` reads them behind an open panel, which hides the page from the tree. */
function shownAddresses(hidden = false) {
  return within(screen.getByRole("table", { name: "Konton", hidden }))
    .getAllByRole("row", { hidden })
    .slice(1)
    .map((row) => within(row).getAllByRole("cell", { hidden })[0]?.textContent);
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("AccountsDirectory (#1974, ADR 0151)", () => {
  it("asks nothing on its own: the server answered the first page", async () => {
    render(<AccountsDirectory initial={FIRST} />);

    await new Promise((resolve) => setTimeout(resolve, 400));
    expect(fetchMock).not.toHaveBeenCalled();
    expect(shownAddresses()).toEqual(["konto.a@example.test", "konto.b@example.test", "konto.c@example.test"]);
    expect(screen.getByRole("status")).toHaveTextContent("3 av 3 konton");
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
    expect(screen.getByRole("status")).toHaveTextContent("1 av 1 konto");
  });

  it("writes no term and no account id into the address bar, the history or storage", async () => {
    const pushState = vi.spyOn(window.history, "pushState");
    const replaceState = vi.spyOn(window.history, "replaceState");
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    fetchMock.mockResolvedValueOnce(json(answer([B])));
    fetchMock.mockResolvedValue(json({ ...B, resumeCount: null, savedSearchCount: null }));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.type(screen.getByRole("searchbox", { name: "Sök på e-postadress" }), "konto.b");
    await waitFor(() => expect(shownAddresses()).toEqual(["konto.b@example.test"]));
    await userEvent.click(screen.getByRole("button", { name: "konto.b@example.test" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));

    const written = JSON.stringify([pushState.mock.calls, replaceState.mock.calls, setItem.mock.calls, window.location.href]);
    for (const value of ["konto.b", B.id]) expect(written).not.toContain(value);
  });

  it("filters and sorts by the backend's names, each from the first page", async () => {
    fetchMock.mockImplementation(async () => json(answer([B, C], [A, B, C])));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.click(screen.getByRole("radio", { name: "Ofullständiga (2)" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(call(0).body).toMatchObject({ status: "ProfileMissing", sort: "RegisteredNewest", page: 1 });

    await userEvent.click(screen.getByRole("button", { name: "Konto" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(call(1).body).toMatchObject({ status: "ProfileMissing", sort: "AddressAscending", page: 1 });
  });

  it("sends each filter and each sort by the backend's own name", async () => {
    fetchMock.mockImplementation(async () => json(answer([A, B, C])));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.click(screen.getByRole("radio", { name: "Aktiva (1)" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    await userEvent.click(screen.getByRole("radio", { name: "Under radering (0)" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    await userEvent.click(screen.getByRole("radio", { name: "Alla (3)" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3));
    await userEvent.click(screen.getByRole("button", { name: "Registrerad" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(4));
    await userEvent.click(screen.getByRole("button", { name: "Konto" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(5));
    await userEvent.click(screen.getByRole("button", { name: "Konto" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(6));

    expect([0, 1, 2].map((index) => call(index).body.status)).toEqual(["Active", "PendingDeletion", undefined]);
    expect([3, 4, 5].map((index) => call(index).body.sort)).toEqual([
      "RegisteredOldest",
      "AddressAscending",
      "AddressDescending",
    ]);
  });

  it.each([
    ["filter", () => userEvent.click(screen.getByRole("radio", { name: /^Aktiva/ })), { status: "Active" }],
    ["sort", () => userEvent.click(screen.getByRole("button", { name: "Konto" })), { sort: "AddressAscending" }],
    ["term", () => userEvent.type(screen.getByRole("searchbox", { name: "Sök på e-postadress" }), "konto"), { address: "konto" }],
  ] as const)("starts again from the first page when the %s changes", async (_, change, sent) => {
    fetchMock.mockImplementation(async (_path, init) => json(directory(JSON.parse(String(init.body)))));
    render(<AccountsDirectory initial={{ kind: "loaded", page: toAccountsPage(directory({ page: 1, pageSize: 25 })) }} />);

    await userEvent.click(within(screen.getByRole("navigation", { name: "Sidnavigering" })).getByRole("button", { name: "Nästa" }));
    await waitFor(() => expect(screen.getByRole("navigation", { name: "Sidnavigering" })).toHaveTextContent("Sida 2 av 3"));
    expect(call(0).body.page).toBe(2);

    await change();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(call(1).body).toMatchObject({ ...sent, page: 1 });
  });

  it("keeps its rows while a newer read is on its way, says so, and aborts a read the next one replaces", async () => {
    fetchMock.mockImplementationOnce(() => new Promise<Response>(() => {}));
    fetchMock.mockResolvedValue(json(answer([B, C], [A, B, C])));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.click(screen.getByRole("radio", { name: "Aktiva (1)" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(screen.getByRole("table", { name: "Konton" })).toHaveAttribute("aria-busy", "true");
    expect(screen.getByRole("status")).toHaveTextContent("Hämtar konton…");
    expect(shownAddresses()).toEqual(["konto.a@example.test", "konto.b@example.test", "konto.c@example.test"]);

    await userEvent.click(screen.getByRole("radio", { name: "Ofullständiga (2)" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(call(0).init?.signal?.aborted).toBe(true);
    await waitFor(() => expect(shownAddresses()).toEqual(["konto.b@example.test", "konto.c@example.test"]));
    expect(screen.getByRole("table", { name: "Konton" })).not.toHaveAttribute("aria-busy");
    expect(screen.getByRole("status")).toHaveTextContent("2 av 3 konton");
  });

  it("words a rate-limited search with its wait, in place of the rows and the counts", async () => {
    fetchMock.mockResolvedValue(json({ error: "rateLimited" }, 429, { "Retry-After": "6" }));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.click(screen.getByRole("radio", { name: "Aktiva (1)" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("För många förfrågningar. Försök igen om 6 sekunder.");
    expect(screen.getByRole("radiogroup", { name: "Visa konton" }).textContent ?? "").not.toMatch(/\d/);
  });

  it("reads the list again when the reader asks after a failed read, and returns focus to the table", async () => {
    fetchMock.mockResolvedValueOnce(json({ error: "error" }, 502));
    fetchMock.mockResolvedValue(json(answer([A], [A, B, C])));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.click(screen.getByRole("radio", { name: "Aktiva (1)" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Kontona kunde inte hämtas. Försök igen om en stund.");
    await userEvent.click(screen.getByRole("button", { name: "Försök igen" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(call(1).body).toEqual(call(0).body);
    await waitFor(() => expect(shownAddresses()).toEqual(["konto.a@example.test"]));
    expect(screen.getByRole("region", { name: "Konton" })).toHaveFocus();
  });

  it("offers a sign-in link instead of a retry once the session has ended, and nothing when the role is gone", async () => {
    fetchMock.mockResolvedValueOnce(json({ error: "unauthorized" }, 401));
    fetchMock.mockResolvedValueOnce(json({ error: "forbidden" }, 403));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.click(screen.getByRole("radio", { name: "Aktiva (1)" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Du är inte inloggad längre.");
    expect(screen.getByRole("link", { name: "Logga in" })).toHaveAttribute("href", "/logga-in");
    expect(screen.queryByRole("button", { name: "Försök igen" })).toBeNull();

    await userEvent.click(screen.getByRole("radio", { name: "Under radering" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Din session saknar Admin-rollen."));
    expect(screen.queryByRole("link", { name: "Logga in" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Försök igen" })).toBeNull();
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

  it("reads an account's details again when the reader asks after a failed read", async () => {
    fetchMock.mockResolvedValueOnce(json({ error: "error" }, 502));
    fetchMock.mockResolvedValue(json(DETAIL));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.click(screen.getByRole("button", { name: "konto.a@example.test" }));
    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    expect(await within(dialog).findByRole("alert")).toHaveTextContent("Kontots uppgifter kunde inte hämtas.");
    await userEvent.click(within(dialog).getByRole("button", { name: "Försök igen" }));

    await waitFor(() => expect(within(dialog).getByText("CV:n").nextElementSibling).toHaveTextContent("2"));
    expect(call(1)).toMatchObject({ path: "/api/admin/konton/detalj", body: { id: A.id } });
  });

  it("says an account that no longer exists is gone, stops describing it, and reads the list again", async () => {
    fetchMock.mockResolvedValueOnce(json({ error: "notFound" }, 404));
    fetchMock.mockResolvedValue(json(answer([B, C])));
    render(<AccountsDirectory initial={FIRST} />);

    await userEvent.click(screen.getByRole("button", { name: "konto.a@example.test" }));
    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    expect(await within(dialog).findByRole("alert")).toHaveTextContent("Kontot finns inte längre.");
    expect(within(dialog).queryByText("Aktiv")).toBeNull();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(call(1).path).toBe("/api/admin/konton");
    await waitFor(() => expect(shownAddresses(true)).toEqual(["konto.b@example.test", "konto.c@example.test"]));

    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(screen.getByRole("region", { name: "Konton" })).toHaveFocus());
  });
});
