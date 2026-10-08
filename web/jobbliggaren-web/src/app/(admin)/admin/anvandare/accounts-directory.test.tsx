import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AccountsListing } from "@/lib/admin/account-directory";
import type { AdminAccessOperation, AdminAccessOutcome } from "@/lib/admin/account-access";
import type { AdminDeletionOutcome } from "@/lib/admin/account-deletion";
import type {
  AdminEmailChangeCancelOutcome,
  AdminEmailChangeRequestOutcome,
} from "@/lib/admin/account-email-change";
import { dismissAdminToast, getAdminToastSnapshot } from "@/lib/admin/toast-store";
import type { AdminSelf } from "@/lib/admin/view-models";
import type { CodeProof, ReauthRequestResult } from "@/lib/auth/reauth-action-state";
import { toAccountsPage, type AccountDetailsDto, type AccountSearchResponse } from "@/lib/dto/admin-accounts";

const { requestActionMock, cancelActionMock, accessActionMock, deletionActionMock, requestReauthCodeMock } = vi.hoisted(() => ({
  requestActionMock: vi.fn<(accountId: string, newEmail: string, proof: CodeProof) => Promise<AdminEmailChangeRequestOutcome>>(),
  cancelActionMock: vi.fn<(accountId: string) => Promise<AdminEmailChangeCancelOutcome>>(),
  accessActionMock: vi.fn<(accountId: string, operation: AdminAccessOperation, proof: CodeProof) => Promise<AdminAccessOutcome>>(),
  deletionActionMock: vi.fn<(accountId: string, proof: CodeProof) => Promise<AdminDeletionOutcome>>(),
  requestReauthCodeMock: vi.fn<() => Promise<ReauthRequestResult>>(),
}));

vi.mock("@/lib/actions/admin-accounts", () => ({
  requestAccountEmailChangeAction: requestActionMock,
  cancelAccountEmailChangeAction: cancelActionMock,
  changeAccountAccessAction: accessActionMock,
  scheduleAccountDeletionAction: deletionActionMock,
}));
vi.mock("@/lib/auth/reauth-actions", () => ({ requestReauthCode: requestReauthCodeMock }));

import { AccountsDirectory } from "./accounts-directory";

/** The signed-in administrator, as the page reads them from the session. */
const SELF: AdminSelf = { userId: "00000000-0000-4000-8000-000000000999", email: "admin@example.test" };

type Item = AccountSearchResponse["accounts"]["items"][number];

const A: Item = {
  id: "00000000-0000-4000-8000-000000000001",
  email: "konto.a@example.test",
  role: "User",
  status: "Active",
  emailConfirmed: true,
  isSuspended: false,
  registeredAt: "2026-09-28T12:02:00Z",
  deletionEarliest: null, deletion: null,
  applicationCount: 4,
};

const B: Item = {
  id: "00000000-0000-4000-8000-000000000002",
  email: "konto.b@example.test",
  role: "User",
  status: "ProfileMissing",
  emailConfirmed: true,
  isSuspended: false,
  registeredAt: null,
  deletionEarliest: null, deletion: null,
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
    suspended: count("Suspended"),
  };
}

function answer(items: ReadonlyArray<Item>, matched: ReadonlyArray<Item> = items): AccountSearchResponse {
  return {
    accounts: { items: [...items], totalCount: items.length, page: 1, pageSize: 25, totalPages: 1 },
    counts: countsOf(matched),
  };
}

const FIRST: AccountsListing = { kind: "loaded", page: toAccountsPage(answer([A, B, C])) };

const DETAIL: AccountDetailsDto = { ...A, resumeCount: 2, deletionPreview: null, savedSearchCount: 3 };

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

/** The calls to one BFF route, in order. */
function callsTo(path: string) {
  return fetchMock.mock.calls
    .filter(([called]) => called === path)
    .map(([, init]) => JSON.parse(String(init.body)) as Record<string, unknown>);
}

const LIST = "/api/admin/konton";
const DETAIL_ROUTE = "/api/admin/konton/detalj";
const EMAIL_CHANGE_ROUTE = "/api/admin/konton/adressbyte";

type Route = () => Response | Promise<Response>;

/** Answers each BFF route by its path, each answer in turn and the last one again once they run out. */
function serve(routes: Partial<Record<string, Route | ReadonlyArray<Route>>>) {
  const served = new Map<string, number>();
  fetchMock.mockImplementation(async (path) => {
    const route = routes[path];
    if (route === undefined) throw new Error(`unexpected path ${path}`);
    const answers = typeof route === "function" ? [route] : route;
    const index = served.get(path) ?? 0;
    served.set(path, index + 1);
    const answer = answers[Math.min(index, answers.length - 1)];
    if (answer === undefined) throw new Error(`no answer for ${path}`);
    return answer();
  });
}

const NOTHING_PENDING = () => json({ pending: null });

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
  requestActionMock.mockReset();
  cancelActionMock.mockReset();
  accessActionMock.mockReset();
  deletionActionMock.mockReset();
  requestReauthCodeMock.mockReset();
  requestReauthCodeMock.mockResolvedValue({ ok: true, challengeId: "step-up-challenge" });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  const toast = getAdminToastSnapshot();
  if (toast !== null) dismissAdminToast(toast.token);
});

describe("AccountsDirectory — scheduled deletion (#1977)", () => {
  const timing = { deletedAt: "2026-10-08T12:00:00Z", eligibleAt: "2026-11-07T12:00:00Z", scheduledRunAt: "2026-11-08T04:00:00Z" };
  const preview = { ...DETAIL, deletionPreview: timing };
  const deleted: Item = { ...A, status: "PendingDeletion", deletionEarliest: "2026-11-07", deletion: timing, applicationCount: null };
  const pending = { pending: { state: "Pending", completableFrom: "2026-10-08T12:00:00Z", expiresAt: "2026-10-09T12:00:00Z" } };

  async function begin(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole("button", { name: A.email! }));
    const panel = await screen.findByRole("dialog", { name: A.email! });
    await within(panel).findByText("CV:n");
    await user.click(within(panel).getByRole("button", { name: "Radera konto" }));
    const dialog = await screen.findByRole("dialog", { name: `Radera ${A.email}?` });
    return { panel, dialog };
  }

  async function prove(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement) {
    await user.click(within(dialog).getByRole("button", { name: `Skicka kod till ${SELF.email}` }));
    const code = await within(dialog).findByLabelText(`Kod till ${SELF.email}`);
    await waitFor(() => expect(code).toHaveFocus());
    await user.type(code, "123456");
    return within(dialog).getByRole("button", { name: "Radera konto" });
  }

  it("shows target, server timeline, real effect and permanent address interruption before asking for an actor code", async () => {
    serve({ [DETAIL_ROUTE]: () => json(preview), [EMAIL_CHANGE_ROUTE]: () => json(pending) });
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);
    const { dialog } = await begin(user);
    expect(dialog).toHaveAccessibleDescription(/30 dagars respit/);
    expect(dialog).toHaveAccessibleDescription(/04:00 UTC/);
    expect(dialog).toHaveAccessibleDescription(/adressbyte avbryts permanent/);
    expect(dialog).toHaveAccessibleDescription(/ingen garanti/);
    expect(dialog).toHaveTextContent(SELF.email);
    expect(requestReauthCodeMock).not.toHaveBeenCalled();
    expect(deletionActionMock).not.toHaveBeenCalled();
    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.getByRole("button", { name: "Radera konto" })).toHaveFocus());
  });

  it("waits for the actual receipt, then refreshes panel, list, counters and permanent address cancellation", async () => {
    serve({
      [DETAIL_ROUTE]: [() => json(preview), () => json({ ...DETAIL, ...deleted })],
      [EMAIL_CHANGE_ROUTE]: [() => json(pending), NOTHING_PENDING],
      [LIST]: () => json(answer([deleted, B, C])),
    });
    let complete: (value: AdminDeletionOutcome) => void = () => {};
    deletionActionMock.mockImplementation(() => new Promise((resolve) => { complete = resolve; }));
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);
    const { panel, dialog } = await begin(user);
    await user.click(await prove(user, dialog));
    expect(within(dialog).getByRole("button", { name: "Schemalägger…" })).toBeDisabled();
    expect(getAdminToastSnapshot()).toBeNull();
    await act(async () => complete({ ok: true, value: { userId: A.id, ...timing } }));
    await within(panel).findByText("Under radering");
    const status = within(within(panel).getByRole("region", { name: "Åtgärder" })).getByRole("status");
    expect(status).toHaveTextContent(`Radering av ${A.email} schemalagd`);
    expect(within(panel).getByText(/Raderingen är ännu inte genomförd/)).toBeInTheDocument();
    await waitFor(() => expect(status).toHaveFocus());
    expect(within(panel).queryByText("Adressbyte")).toBeNull();
    expect(deletionActionMock).toHaveBeenCalledExactlyOnceWith(A.id, { challengeId: "step-up-challenge", code: "123456" });
    await waitFor(() => expect(callsTo(LIST)).toHaveLength(1));
    expect(callsTo(DETAIL_ROUTE)).toHaveLength(2);
    expect(getAdminToastSnapshot()).toBeNull();
    await user.keyboard("{Escape}");
    expect(await screen.findByRole("radio", { name: "Under radering (1)" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Aktiva (0)" })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Konton" })).toHaveTextContent("Planerad körning");
  });

  it("focuses a known no-op refusal without any success receipt", async () => {
    const refusal = "Kontot väntar redan på radering. Datumet har inte ändrats.";
    serve({ [DETAIL_ROUTE]: [() => json(preview), () => json({ ...DETAIL, ...deleted })],
      [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING, [LIST]: () => json(answer([deleted])) });
    deletionActionMock.mockResolvedValue({ ok: false, kind: "operationRefused", channel: "status", error: refusal });
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);
    const { panel, dialog } = await begin(user);
    await user.click(await prove(user, dialog));
    const alert = await within(within(panel).getByRole("region", { name: "Åtgärder" })).findByRole("alert");
    expect(alert).toHaveTextContent(refusal);
    await waitFor(() => expect(alert).toHaveFocus());
    expect(getAdminToastSnapshot()).toBeNull();
  });

  it("keeps a lost outcome unknown until explicit status reread and never replays deletion", async () => {
    const uncertainty = "Det går inte att bekräfta om raderingen schemalades.";
    serve({ [DETAIL_ROUTE]: [() => json(preview), () => json({ ...DETAIL, ...deleted })],
      [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING, [LIST]: () => json(answer([deleted])) });
    deletionActionMock.mockResolvedValue({ ok: false, kind: "outcomeUnknown", error: uncertainty });
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);
    const { panel, dialog } = await begin(user);
    await user.click(await prove(user, dialog));
    const status = await within(within(panel).getByRole("region", { name: "Åtgärder" })).findByRole("status");
    expect(status).toHaveTextContent(uncertainty);
    await waitFor(() => expect(status).toHaveFocus());
    expect(callsTo(DETAIL_ROUTE)).toHaveLength(1);
    expect(callsTo(LIST)).toHaveLength(0);
    expect(getAdminToastSnapshot()).toBeNull();
    await user.click(within(panel).getByRole("button", { name: "Läs in kontots status" }));
    await within(panel).findByText("Under radering");
    await waitFor(() => expect(callsTo(LIST)).toHaveLength(1));
    expect(deletionActionMock).toHaveBeenCalledTimes(1);
    expect(getAdminToastSnapshot()).toBeNull();
  });
});

describe("AccountsDirectory (#1974, ADR 0151)", () => {
  it("asks nothing on its own: the server answered the first page", async () => {
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    await new Promise((resolve) => setTimeout(resolve, 400));
    expect(fetchMock).not.toHaveBeenCalled();
    expect(shownAddresses()).toEqual(["konto.a@example.test", "konto.b@example.test", "konto.c@example.test"]);
    expect(screen.getByRole("status")).toHaveTextContent("3 av 3 konton");
  });

  it("searches once typing pauses, with the term in the request body and never in its URL", async () => {
    fetchMock.mockResolvedValue(json(answer([B])));
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

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
    serve({
      [LIST]: () => json(answer([B])),
      [DETAIL_ROUTE]: () => json({ ...B, resumeCount: null, savedSearchCount: null }),
      [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING,
    });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    await userEvent.type(screen.getByRole("searchbox", { name: "Sök på e-postadress" }), "konto.b");
    await waitFor(() => expect(shownAddresses()).toEqual(["konto.b@example.test"]));
    await userEvent.click(screen.getByRole("button", { name: "konto.b@example.test" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3));

    const written = JSON.stringify([pushState.mock.calls, replaceState.mock.calls, setItem.mock.calls, window.location.href]);
    for (const value of ["konto.b", B.id]) expect(written).not.toContain(value);
  });

  it("filters and sorts by the backend's names, each from the first page", async () => {
    fetchMock.mockImplementation(async () => json(answer([B, C], [A, B, C])));
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    await userEvent.click(screen.getByRole("radio", { name: "Ofullständiga (2)" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(call(0).body).toMatchObject({ status: "ProfileMissing", sort: "RegisteredNewest", page: 1 });

    await userEvent.click(screen.getByRole("button", { name: "Konto" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(call(1).body).toMatchObject({ status: "ProfileMissing", sort: "AddressAscending", page: 1 });
  });

  it("sends each filter and each sort by the backend's own name", async () => {
    fetchMock.mockImplementation(async () => json(answer([A, B, C])));
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

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
    render(<AccountsDirectory initial={{ kind: "loaded", page: toAccountsPage(directory({ page: 1, pageSize: 25 })) }} self={SELF} />);

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
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

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
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    await userEvent.click(screen.getByRole("radio", { name: "Aktiva (1)" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("För många förfrågningar. Försök igen om 6 sekunder.");
    expect(screen.getByRole("radiogroup", { name: "Visa konton" }).textContent ?? "").not.toMatch(/\d/);
  });

  it("reads the list again when the reader asks after a failed read, and returns focus to the table", async () => {
    fetchMock.mockResolvedValueOnce(json({ error: "error" }, 502));
    fetchMock.mockResolvedValue(json(answer([A], [A, B, C])));
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

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
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

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
    serve({
      [DETAIL_ROUTE]: () => new Promise<Response>((resolve) => (arrive = resolve)),
      [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING,
    });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    await userEvent.click(screen.getByRole("button", { name: "konto.a@example.test" }));
    const dialog = await screen.findByRole("dialog", { name: "konto.a@example.test" });
    expect(within(dialog).getByRole("status")).toHaveTextContent("Hämtar kontots uppgifter…");
    const { path, body } = call(0);
    expect(path).toBe(DETAIL_ROUTE);
    expect(body).toEqual({ id: A.id });

    arrive(json(DETAIL));
    await waitFor(() => expect(within(dialog).getByText("CV:n").nextElementSibling).toHaveTextContent("2"));
    expect(within(dialog).getByText("Sparade sökningar").nextElementSibling).toHaveTextContent("3");
    expect(within(dialog).queryByText("Adressbyte")).toBeNull();
  });

  it("reads an account's details again when the reader asks after a failed read", async () => {
    serve({
      [DETAIL_ROUTE]: [() => json({ error: "error" }, 502), () => json(DETAIL)],
      [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING,
    });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    await userEvent.click(screen.getByRole("button", { name: "konto.a@example.test" }));
    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    expect(await within(dialog).findByRole("alert")).toHaveTextContent("Kontots uppgifter kunde inte hämtas.");
    await userEvent.click(within(dialog).getByRole("button", { name: "Försök igen" }));

    await waitFor(() => expect(within(dialog).getByText("CV:n").nextElementSibling).toHaveTextContent("2"));
    expect(callsTo(DETAIL_ROUTE)).toEqual([{ id: A.id }, { id: A.id }]);
  });

  it("says an account that no longer exists is gone, stops describing it, and reads the list again", async () => {
    serve({
      [DETAIL_ROUTE]: () => json({ error: "notFound" }, 404),
      [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING,
      [LIST]: () => json(answer([B, C])),
    });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    await userEvent.click(screen.getByRole("button", { name: "konto.a@example.test" }));
    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    expect(await within(dialog).findByRole("alert")).toHaveTextContent("Kontot finns inte längre.");
    expect(within(dialog).queryByText("Aktiv")).toBeNull();
    await waitFor(() => expect(callsTo(LIST)).toHaveLength(1));
    await waitFor(() => expect(shownAddresses(true)).toEqual(["konto.b@example.test", "konto.c@example.test"]));

    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(screen.getByRole("region", { name: "Konton" })).toHaveFocus());
  });
});

describe("AccountsDirectory — an account's address change (#1975, ADR 0153)", () => {
  const PENDING_READ = {
    pending: { state: "Pending", completableFrom: "2026-10-08T12:00:00+00:00", expiresAt: "2026-10-09T12:00:00+00:00" },
  };
  const PENDING_LINE = "Väntar på kontoägaren. Koden kan användas från 2026-10-08 14:00 till 2026-10-09 14:00.";
  const NEW = "ny.adress@example.test";

  /** The text a reader sees: a busy label's hidden form is not part of it. */
  function shownText(node: Node): string {
    if (node instanceof Element && node.getAttribute("aria-hidden") === "true") return "";
    if (node.nodeType === Node.TEXT_NODE) return node.textContent ?? "";
    return [...node.childNodes].map(shownText).join("");
  }

  function actionNames(dialog: HTMLElement) {
    return within(within(dialog).getByRole("region", { name: "Åtgärder" }))
      .getAllByRole("button")
      .map(shownText);
  }

  async function openA() {
    await userEvent.click(screen.getByRole("button", { name: "konto.a@example.test" }));
    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
    await within(dialog).findByText("CV:n");
    return dialog;
  }

  /** Types the new address, goes through the administrator's step-up and presses its primary. */
  async function requestChange(user: ReturnType<typeof userEvent.setup>, panel: HTMLElement) {
    await user.click(within(panel).getByRole("button", { name: "Ändra e-postadress" }));
    await user.type(within(panel).getByLabelText("Ny e-postadress"), NEW);
    await user.click(within(panel).getByRole("button", { name: "Fortsätt" }));
    const stepUp = await screen.findByRole("dialog", { name: "Ändra e-postadress" });
    await user.click(within(stepUp).getByRole("button", { name: "Skicka kod" }));
    await user.type(await within(stepUp).findByLabelText("Sexsiffrig kod"), "123456");
    await user.click(within(stepUp).getByRole("button", { name: "Bekräfta koden" }));
    return stepUp;
  }

  it("reads the pending change beside the details, by a body, and states it as a fact with its cancel", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: () => json(PENDING_READ) });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const dialog = await openA();

    expect(callsTo(EMAIL_CHANGE_ROUTE)).toEqual([{ id: A.id }]);
    expect(fetchMock.mock.calls.find(([path]) => path === EMAIL_CHANGE_ROUTE)?.[1].cache).toBe("no-store");
    expect(within(dialog).getByText("Adressbyte").nextElementSibling).toHaveTextContent(PENDING_LINE);
    expect(actionNames(dialog)).toContain("Avbryt adressbytet");
    expect(actionNames(dialog)).not.toContain("Ändra e-postadress");
  });

  it("costs only the address change's fact when its read fails", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: () => json({ error: "error" }, 502) });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const dialog = await openA();

    expect(within(dialog).getByText("CV:n").nextElementSibling).toHaveTextContent("2");
    expect(within(dialog).getByText("Adressbyte").nextElementSibling?.querySelector(".sr-only")).toHaveTextContent(
      "Uppgift saknas",
    );
    expect(within(dialog).queryByRole("alert")).toBeNull();
  });

  it("offers address change and suspension live, with the remaining actions as Kommer snart", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const dialog = await openA();

    expect(actionNames(dialog)).toEqual([
      "Agera som användaren Kommer snart",
      "Ändra e-postadress",
      "Skicka inloggningslänk Kommer snart",
      "Stäng av åtkomst",
      "Radera konto Kommer snart",
      "Radera permanent Kommer snart",
    ]);
  });

  it("tells the administrator's own account by the session's id, and points it to Mina sidor", async () => {
    const own = { ...A, id: SELF.userId, email: SELF.email, role: "Admin" as const };
    serve({
      [DETAIL_ROUTE]: () => json({ ...own, resumeCount: 2, deletionPreview: null, savedSearchCount: 3 }),
      [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING,
    });
    render(<AccountsDirectory initial={{ kind: "loaded", page: toAccountsPage(answer([own, B])) }} self={SELF} />);

    await userEvent.click(screen.getByRole("button", { name: SELF.email }));
    const dialog = screen.getByRole("dialog", { name: SELF.email });

    expect(
      await within(dialog).findByText("Adressen på ett administratörskonto byts inte här. Byt din på Mina sidor."),
    ).toBeInTheDocument();
    expect(actionNames(dialog)).not.toContain("Ändra e-postadress");
  });

  it("requests through its Server Action with the administrator's step-up, and shows the pending change it answers", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING });
    requestActionMock.mockResolvedValue({
      ok: true,
      value: { state: "pending", completableFrom: "2026-10-08T12:00:00Z", expiresAt: "2026-10-09T12:00:00Z" },
    });
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const panel = await openA();
    await requestChange(user, panel);

    await waitFor(() => expect(within(panel).getByText("Adressbyte").nextElementSibling).toHaveTextContent(PENDING_LINE));
    expect(requestReauthCodeMock).toHaveBeenCalledTimes(1);
    expect(requestActionMock).toHaveBeenCalledWith(A.id, NEW, { challengeId: "step-up-challenge", code: "123456" });
    // The pending change came from the action's answer; the account was not read again for it.
    expect(callsTo(EMAIL_CHANGE_ROUTE)).toHaveLength(1);
  });

  it.each(["changed", "refetch"] as const)("reads the account again after a refusal it answers with %s", async (after) => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING });
    requestActionMock.mockResolvedValue({ ok: false, kind: "operationRefused", error: "Nej.", channel: "status", after });
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const panel = await openA();
    await requestChange(user, panel);

    await waitFor(() => expect(callsTo(DETAIL_ROUTE)).toHaveLength(2));
    expect(callsTo(EMAIL_CHANGE_ROUTE)).toHaveLength(2);
  });

  it("cancels through its Server Action, and the pending change goes with it", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: () => json(PENDING_READ) });
    cancelActionMock.mockResolvedValue({ kind: "cancelled" });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const panel = await openA();
    await userEvent.click(within(panel).getByRole("button", { name: "Avbryt adressbytet" }));

    await waitFor(() => expect(within(panel).queryByText("Adressbyte")).toBeNull());
    expect(cancelActionMock).toHaveBeenCalledWith(A.id);
    expect(getAdminToastSnapshot()?.message).toBe(
      "Adressbytet är avbrutet. Koden till den nya adressen gäller inte längre.",
    );
  });

  it("reads the account again when there was nothing left to cancel", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: [() => json(PENDING_READ), NOTHING_PENDING] });
    cancelActionMock.mockResolvedValue({ kind: "nothingPending" });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const panel = await openA();
    await userEvent.click(within(panel).getByRole("button", { name: "Avbryt adressbytet" }));

    expect(await within(panel).findByText("Det finns inget adressbyte att avbryta längre.")).toBeInTheDocument();
    await waitFor(() => expect(within(panel).queryByText("Adressbyte")).toBeNull());
    expect(callsTo(EMAIL_CHANGE_ROUTE)).toHaveLength(2);
  });

  const READ_FAILED = () => json({ error: "error" }, 502);
  const UNREADABLE = "Det går inte att se om ett adressbyte väntar.";
  const CANCEL_UNKNOWN = "Vi kan inte se om adressbytet avbröts. Öppna kontot igen för att se om det väntar.";

  function retryIn(panel: HTMLElement) {
    return within(within(panel).getByRole("region", { name: "Åtgärder" })).getByRole("button", { name: "Försök igen" });
  }

  it("reads the change again at the reader's request when its read failed, and moves focus to the request when nothing is pending", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: [READ_FAILED, NOTHING_PENDING] });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const panel = await openA();
    expect(actionNames(panel)).not.toContain("Ändra e-postadress");
    const retry = retryIn(panel);
    expect(retry).toHaveAccessibleDescription(UNREADABLE);
    await userEvent.click(retry);

    await waitFor(() => expect(within(panel).getByRole("button", { name: "Ändra e-postadress" })).toHaveFocus());
    expect(callsTo(EMAIL_CHANGE_ROUTE)).toEqual([{ id: A.id }, { id: A.id }]);
  });

  it("moves focus to the pending change's value when the read again finds one, never to its cancel", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: [READ_FAILED, () => json(PENDING_READ)] });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const panel = await openA();
    await userEvent.click(retryIn(panel));

    const value = within(panel).getByText("Adressbyte").nextElementSibling;
    await waitFor(() => expect(value).toHaveTextContent(PENDING_LINE));
    await waitFor(() => expect(value).toHaveFocus());
    expect(within(panel).getByRole("button", { name: "Avbryt adressbytet" })).not.toHaveFocus();
  });

  it("reads the change once however often Försök igen is pressed while the read runs", async () => {
    serve({
      [DETAIL_ROUTE]: () => json(DETAIL),
      [EMAIL_CHANGE_ROUTE]: [READ_FAILED, () => new Promise<Response>(() => {})],
    });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const panel = await openA();
    const retry = retryIn(panel);
    await userEvent.click(retry);
    expect(retry).toHaveAccessibleName("Hämtar…");
    await userEvent.click(retry);
    await userEvent.keyboard("{Enter}");

    expect(callsTo(EMAIL_CHANGE_ROUTE)).toEqual([{ id: A.id }, { id: A.id }]);
  });

  it.each([
    ["nothing is pending", NOTHING_PENDING, (panel: HTMLElement) => within(panel).getByRole("button", { name: "Ändra e-postadress" })],
    ["a change is pending", () => json(PENDING_READ), (panel: HTMLElement) => within(panel).getByText("Adressbyte").nextElementSibling],
  ] as const)(
    "drops what a cancel with an unknown outcome said once Försök igen reads the change (%s)",
    async (_label, again, focused) => {
      serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: [() => json(PENDING_READ), again] });
      cancelActionMock.mockResolvedValue({ kind: "unknown" });
      render(<AccountsDirectory initial={FIRST} self={SELF} />);

      const panel = await openA();
      await userEvent.click(within(panel).getByRole("button", { name: "Avbryt adressbytet" }));
      const status = await within(panel).findByText(CANCEL_UNKNOWN);
      await waitFor(() => expect(status).toHaveFocus());
      await userEvent.click(retryIn(panel));

      await waitFor(() => expect(within(panel).queryByText(CANCEL_UNKNOWN)).toBeNull());
      await waitFor(() => expect(focused(panel)).toHaveFocus());
    },
  );

  it("keeps what a cancel with an unknown outcome said, and moves focus back to Försök igen, when the read fails again", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: [() => json(PENDING_READ), READ_FAILED] });
    cancelActionMock.mockResolvedValue({ kind: "unknown" });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const panel = await openA();
    await userEvent.click(within(panel).getByRole("button", { name: "Avbryt adressbytet" }));
    await within(panel).findByText(CANCEL_UNKNOWN);
    await userEvent.click(retryIn(panel));

    await waitFor(() => expect(retryIn(panel)).toHaveFocus());
    expect(retryIn(panel)).toHaveAccessibleDescription(UNREADABLE);
    expect(within(panel).getByText(CANCEL_UNKNOWN)).toBeInTheDocument();
    expect(callsTo(EMAIL_CHANGE_ROUTE)).toHaveLength(2);
  });

  it("moves focus to the title when Försök igen finds the account gone", async () => {
    serve({
      [DETAIL_ROUTE]: [() => json(DETAIL), () => json({ error: "notFound" }, 404)],
      [EMAIL_CHANGE_ROUTE]: [READ_FAILED, () => json({ error: "notFound" }, 404)],
      [LIST]: () => json(answer([B, C])),
    });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const panel = await openA();
    await userEvent.click(retryIn(panel));

    expect(await within(panel).findByRole("alert")).toHaveTextContent("Kontot finns inte längre.");
    await waitFor(() => expect(within(panel).getByRole("heading", { name: "konto.a@example.test" })).toHaveFocus());
  });

  it("moves focus to what a cancel with an unknown outcome says, and offers neither a request nor a cancel", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: () => json(PENDING_READ) });
    cancelActionMock.mockResolvedValue({ kind: "unknown" });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const panel = await openA();
    await userEvent.click(within(panel).getByRole("button", { name: "Avbryt adressbytet" }));

    const status = await within(panel).findByText(
      "Vi kan inte se om adressbytet avbröts. Öppna kontot igen för att se om det väntar.",
    );
    expect(status).toHaveAttribute("role", "status");
    await waitFor(() => expect(status).toHaveFocus());
    expect(within(panel).getByText("Adressbyte").nextElementSibling?.querySelector(".sr-only")).toHaveTextContent(
      "Uppgift saknas",
    );
    expect(actionNames(panel)).not.toContain("Ändra e-postadress");
    expect(actionNames(panel)).not.toContain("Avbryt adressbytet");
    expect(within(panel).getByText("Det går inte att se om ett adressbyte väntar.")).toBeInTheDocument();
  });

  it("claims nothing about a request whose outcome is unknown, and offers no second request while it cannot tell", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING });
    const unknown = "Vi kan inte se om koden skickades. Öppna kontot igen för att se om ett adressbyte väntar.";
    requestActionMock.mockResolvedValue({ ok: false, kind: "outcomeUnknown", error: unknown });
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const panel = await openA();
    await requestChange(user, panel);

    const status = await within(panel).findByText(unknown);
    await waitFor(() => expect(status).toHaveFocus());
    expect(within(panel).getByText("Adressbyte").nextElementSibling?.querySelector(".sr-only")).toHaveTextContent(
      "Uppgift saknas",
    );
    expect(actionNames(panel)).not.toContain("Ändra e-postadress");
    expect(within(panel).getByText("Det går inte att se om ett adressbyte väntar.")).toBeInTheDocument();
    expect(callsTo(EMAIL_CHANGE_ROUTE)).toHaveLength(1);
  });

  it("says an account a request's refusal found gone is gone, and reads the list again", async () => {
    serve({
      [DETAIL_ROUTE]: () => json(DETAIL),
      [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING,
      [LIST]: () => json(answer([B, C])),
    });
    requestActionMock.mockResolvedValue({
      ok: false,
      kind: "operationRefused",
      error: "Kontot finns inte längre.",
      channel: "status",
      after: "gone",
    });
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const panel = await openA();
    await requestChange(user, panel);

    expect(await within(panel).findByRole("alert")).toHaveTextContent("Kontot finns inte längre.");
    expect(within(panel).queryByRole("region", { name: "Åtgärder" })).toBeNull();
    await waitFor(() => expect(callsTo(LIST)).toHaveLength(1));
    await waitFor(() => expect(shownAddresses(true)).toEqual(["konto.b@example.test", "konto.c@example.test"]));
    expect(callsTo(DETAIL_ROUTE)).toHaveLength(1);
  });

  it("drops a cancel's answer that arrives after the panel has opened another account", async () => {
    const D_EMAIL = "konto.d@example.test";
    const D: Item = { ...A, id: "00000000-0000-4000-8000-000000000004", email: D_EMAIL };
    fetchMock.mockImplementation(async (path, init) => {
      const { id } = JSON.parse(String(init.body)) as { readonly id: string };
      if (path === DETAIL_ROUTE) return json({ ...(id === D.id ? D : A), resumeCount: 2, deletionPreview: null, savedSearchCount: 3 });
      if (path === EMAIL_CHANGE_ROUTE) return json(PENDING_READ);
      throw new Error(`unexpected path ${path}`);
    });
    let settle: (outcome: AdminEmailChangeCancelOutcome) => void = () => {};
    cancelActionMock.mockImplementation(() => new Promise((resolve) => (settle = resolve)));
    render(<AccountsDirectory initial={{ kind: "loaded", page: toAccountsPage(answer([A, D])) }} self={SELF} />);

    const panelA = await openA();
    await userEvent.click(within(panelA).getByRole("button", { name: "Avbryt adressbytet" }));
    expect(cancelActionMock).toHaveBeenCalledWith(A.id);
    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await userEvent.click(screen.getByRole("button", { name: D_EMAIL }));
    const panelD = screen.getByRole("dialog", { name: D_EMAIL });
    await waitFor(() => expect(within(panelD).getByText("Adressbyte").nextElementSibling).toHaveTextContent(PENDING_LINE));

    await act(async () => settle({ kind: "cancelled" }));

    expect(within(panelD).getByText("Adressbyte").nextElementSibling).toHaveTextContent(PENDING_LINE);
    expect(actionNames(panelD)).toContain("Avbryt adressbytet");
    expect(callsTo(DETAIL_ROUTE)).toEqual([{ id: A.id }, { id: D.id }]);
    expect(callsTo(EMAIL_CHANGE_ROUTE)).toEqual([{ id: A.id }, { id: D.id }]);
  });
});

describe("AccountsDirectory — suspend and reinstate access (#1976)", () => {
  const SUSPENDED: Item = { ...A, status: "Suspended", isSuspended: true };
  const DELETING: Item = { ...SUSPENDED, status: "PendingDeletion", deletionEarliest: "2026-11-07" };
  const PENDING_READ = {
    pending: { state: "Pending", completableFrom: "2026-10-08T12:00:00Z", expiresAt: "2026-10-09T12:00:00Z" },
  };
  const SPENT = "Koden du skrev in är förbrukad, så du behöver en ny kod när du försöker igen.";
  const receipt = (isSuspended: boolean, pendingDeletion = false): AdminAccessOutcome => ({
    ok: true, value: { userId: A.id, isSuspended, accessRevision: isSuspended ? 1 : 2, pendingDeletion },
  });

  async function openAccount() {
    await userEvent.click(screen.getByRole("button", { name: A.email! }));
    const panel = await screen.findByRole("dialog", { name: A.email! });
    await within(panel).findByText("CV:n");
    return panel;
  }

  async function beginAccess(user: ReturnType<typeof userEvent.setup>, panel: HTMLElement, operation: AdminAccessOperation) {
    const action = operation === "suspend" ? "Stäng av åtkomst" : "Återaktivera åtkomst";
    await user.click(within(panel).getByRole("button", { name: action }));
    const dialog = await screen.findByRole("dialog", {
      name: operation === "suspend" ? `Stäng av åtkomsten för ${A.email}?` : `Återaktivera åtkomsten för ${A.email}?`,
    });
    await user.click(within(dialog).getByRole("button", { name: "Skicka kod" }));
    const code = await within(dialog).findByLabelText("Sexsiffrig kod");
    await waitFor(() => expect(code).toHaveFocus());
    await user.type(code, "123456");
    return { dialog, submit: within(dialog).getByRole("button", { name: action }) };
  }

  it("confirms suspension and permanent cancellation of the pending address change before requesting any code", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: () => json(PENDING_READ) });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);
    const panel = await openAccount();
    const trigger = within(panel).getByRole("button", { name: "Stäng av åtkomst" });

    await userEvent.click(trigger);

    const dialog = screen.getByRole("dialog", { name: `Stäng av åtkomsten för ${A.email}?` });
    expect(dialog).toHaveAccessibleDescription(/Det väntande adressbytet avbryts\./);
    expect(dialog).toHaveTextContent("Alla sessioner avslutas.");
    expect(dialog).toHaveTextContent(SELF.email);
    expect(requestReauthCodeMock).not.toHaveBeenCalled();
    expect(accessActionMock).not.toHaveBeenCalled();
    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(trigger).toHaveFocus());
    expect(within(panel).getByRole("button", { name: "Avbryt adressbytet" })).toBeInTheDocument();
    expect(getAdminToastSnapshot()).toBeNull();
  });

  it("suspends through the real code dialog and refreshes the panel, list, counters and address change from server answers", async () => {
    serve({
      [DETAIL_ROUTE]: [() => json(DETAIL), () => json({ ...DETAIL, ...SUSPENDED })],
      [EMAIL_CHANGE_ROUTE]: [() => json(PENDING_READ), NOTHING_PENDING],
      [LIST]: () => json(answer([SUSPENDED, B, C])),
    });
    accessActionMock.mockResolvedValue(receipt(true));
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);
    const panel = await openAccount();
    const { submit } = await beginAccess(user, panel, "suspend");

    await user.click(submit);

    expect(accessActionMock).toHaveBeenCalledExactlyOnceWith(A.id, "suspend", {
      challengeId: "step-up-challenge", code: "123456",
    });
    await waitFor(() => expect(within(panel).getByRole("button", { name: "Återaktivera åtkomst" })).toBeInTheDocument());
    expect(within(panel).getByText("Avstängd")).toBeInTheDocument();
    expect(within(panel).queryByText("Adressbyte")).toBeNull();
    expect(within(panel).queryByRole("button", { name: "Stäng av åtkomst" })).toBeNull();
    await waitFor(() => expect(callsTo(LIST)).toHaveLength(1));
    expect(callsTo(DETAIL_ROUTE)).toEqual([{ id: A.id }, { id: A.id }]);
    expect(callsTo(EMAIL_CHANGE_ROUTE)).toEqual([{ id: A.id }, { id: A.id }]);
    expect(getAdminToastSnapshot()?.message).toBe(`Åtkomsten för ${A.email} är avstängd.`);
    await waitFor(() => expect(within(panel).getByRole("heading", { name: A.email! })).toHaveFocus());

    await user.keyboard("{Escape}");
    expect(await screen.findByRole("radio", { name: "Avstängda (1)" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Aktiva (0)" })).toBeInTheDocument();
    expect(within(screen.getByRole("table", { name: "Konton" })).getAllByRole("row")[1]).toHaveTextContent("Avstängd");
  });

  it("reinstates a suspended account under deletion with its own code confirmation and a receipt that deletion continues", async () => {
    const reinstated: Item = { ...DELETING, isSuspended: false };
    serve({
      [DETAIL_ROUTE]: [() => json({ ...DETAIL, ...DELETING }), () => json({ ...DETAIL, ...reinstated })],
      [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING,
      [LIST]: () => json(answer([reinstated])),
    });
    accessActionMock.mockResolvedValue(receipt(false, true));
    const user = userEvent.setup();
    render(<AccountsDirectory initial={{ kind: "loaded", page: toAccountsPage(answer([DELETING])) }} self={SELF} />);
    const panel = await openAccount();
    expect(within(panel).getByText("Åtkomst").nextElementSibling).toHaveTextContent("Avstängd");
    const { dialog, submit } = await beginAccess(user, panel, "reinstate");
    expect(dialog).toHaveAccessibleDescription(/Tidigare sessioner återaktiveras inte\./);
    expect(dialog).toHaveAccessibleDescription(/Raderingen fortsätter\./);

    await user.click(submit);

    expect(accessActionMock).toHaveBeenCalledExactlyOnceWith(A.id, "reinstate", {
      challengeId: "step-up-challenge", code: "123456",
    });
    await waitFor(() => expect(within(panel).queryByText("Åtkomst")).toBeNull());
    expect(within(panel).getByText("Under radering")).toBeInTheDocument();
    expect(within(panel).getByText("Raderas slutgiltigt").nextElementSibling).toHaveTextContent("2026-11-07");
    expect(getAdminToastSnapshot()?.message).toBe(
      `Åtkomsten för ${A.email} är återaktiverad. Kontoägaren behöver logga in igen. Raderingen fortsätter.`,
    );
  });

  it("shows and focuses the direct reinstatement command's refusal in Åtgärder and publishes no success receipt (#1994)", async () => {
    const refusal = `Kontots åtkomst är redan återaktiverad. ${SPENT}`;
    serve({
      [DETAIL_ROUTE]: [() => json({ ...DETAIL, ...SUSPENDED }), () => json(DETAIL)],
      [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING,
      [LIST]: () => json(answer([A])),
    });
    // Another administrator reinstated the account after the panel read it; the command returns its real no-op refusal.
    accessActionMock.mockResolvedValue({ ok: false, kind: "operationRefused", channel: "status", error: refusal });
    const user = userEvent.setup();
    render(<AccountsDirectory initial={{ kind: "loaded", page: toAccountsPage(answer([SUSPENDED])) }} self={SELF} />);
    const panel = await openAccount();
    const { submit } = await beginAccess(user, panel, "reinstate");

    await user.click(submit);

    const actions = within(panel).getByRole("region", { name: "Åtgärder" });
    const alert = await within(actions).findByRole("alert");
    expect(alert).toHaveTextContent(refusal);
    await waitFor(() => expect(alert).toHaveFocus());
    expect(accessActionMock).toHaveBeenCalledExactlyOnceWith(A.id, "reinstate", {
      challengeId: "step-up-challenge", code: "123456",
    });
    expect(getAdminToastSnapshot()).toBeNull();
    expect(callsTo(DETAIL_ROUTE)).toHaveLength(2);
  });

  it("keeps an unknown command outcome uncertain even when a later read finds the account suspended", async () => {
    const uncertainty = `Det går inte att bekräfta om åtgärden genomfördes. ${SPENT}`;
    serve({
      [DETAIL_ROUTE]: [() => json(DETAIL), () => json({ ...DETAIL, ...SUSPENDED })],
      [EMAIL_CHANGE_ROUTE]: [() => json(PENDING_READ), NOTHING_PENDING],
      [LIST]: () => json(answer([SUSPENDED])),
    });
    accessActionMock.mockResolvedValue({ ok: false, kind: "outcomeUnknown", error: uncertainty });
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);
    const panel = await openAccount();
    const { submit } = await beginAccess(user, panel, "suspend");

    await user.click(submit);

    const status = await within(within(panel).getByRole("region", { name: "Åtgärder" })).findByRole("status");
    expect(status).toHaveTextContent(uncertainty);
    await waitFor(() => expect(status).toHaveFocus());
    expect(await within(panel).findByText("Avstängd")).toBeInTheDocument();
    expect(getAdminToastSnapshot()).toBeNull();
    expect(accessActionMock).toHaveBeenCalledTimes(1);
  });

  it("keeps a wrong code at the focused code field and spends no second challenge", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING });
    const error = "Koden stämmer inte. Kontrollera siffrorna och försök igen.";
    accessActionMock.mockResolvedValue({ ok: false, kind: "wrongCode", error });
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);
    const panel = await openAccount();
    const { dialog, submit } = await beginAccess(user, panel, "suspend");

    await user.click(submit);

    const code = within(dialog).getByLabelText("Sexsiffrig kod");
    await waitFor(() => expect(code).toHaveFocus());
    expect(code).toHaveValue("");
    expect(code).toHaveAttribute("aria-invalid", "true");
    expect(code).toHaveAccessibleDescription(/Koden stämmer inte\. Kontrollera siffrorna och försök igen\./);
    expect(requestReauthCodeMock).toHaveBeenCalledTimes(1);
    expect(callsTo(DETAIL_ROUTE)).toHaveLength(1);
    expect(getAdminToastSnapshot()).toBeNull();
  });

  it("returns a rate-limited command to Åtgärder with the spent-code message and no receipt", async () => {
    const error = `För många förfrågningar. Försök igen om 6 sekunder. ${SPENT}`;
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING, [LIST]: () => json(answer([A])) });
    accessActionMock.mockResolvedValue({ ok: false, kind: "operationRefused", channel: "status", error });
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);
    const panel = await openAccount();
    const { submit } = await beginAccess(user, panel, "suspend");

    await user.click(submit);

    const alert = await within(within(panel).getByRole("region", { name: "Åtgärder" })).findByRole("alert");
    expect(alert).toHaveTextContent(error);
    await waitFor(() => expect(alert).toHaveFocus());
    expect(getAdminToastSnapshot()).toBeNull();
  });

  it("keeps session expiry in the code dialog with a focused status and the return-to-admin login link", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING });
    accessActionMock.mockResolvedValue({ ok: false, kind: "notLoggedIn" });
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);
    const panel = await openAccount();
    const { dialog, submit } = await beginAccess(user, panel, "suspend");

    await user.click(submit);

    const status = await within(dialog).findByRole("status");
    expect(status).toHaveTextContent("Du är inte inloggad längre. Logga in igen och börja om.");
    await waitFor(() => expect(status).toHaveFocus());
    expect(within(dialog).getByRole("link", { name: "Logga in" })).toHaveAttribute("href", "/logga-in?next=/admin/anvandare");
    expect(callsTo(DETAIL_ROUTE)).toHaveLength(1);
    expect(getAdminToastSnapshot()).toBeNull();
  });

  it("disables a pending access write and keeps its dialog open until the command answers", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING, [LIST]: () => json(answer([A])) });
    let settle: (outcome: AdminAccessOutcome) => void = () => {};
    accessActionMock.mockImplementation(() => new Promise((resolve) => (settle = resolve)));
    const user = userEvent.setup();
    render(<AccountsDirectory initial={FIRST} self={SELF} />);
    const panel = await openAccount();
    const { dialog, submit } = await beginAccess(user, panel, "suspend");

    await user.click(submit);

    expect(within(dialog).getByRole("button", { name: "Stänger av…" })).toBeDisabled();
    expect(within(dialog).getByRole("button", { name: "Avbryt" })).toBeDisabled();
    await user.keyboard("{Escape}{Enter}");
    expect(dialog).toBeInTheDocument();
    expect(accessActionMock).toHaveBeenCalledTimes(1);
    expect(getAdminToastSnapshot()).toBeNull();
    await act(async () => settle({ ok: false, kind: "operationRefused", channel: "status", error: "Kontots åtkomst är redan avstängd." }));
    expect(await within(panel).findByRole("alert")).toHaveTextContent("Kontots åtkomst är redan avstängd.");
  });

  it("sends the suspended filter under its backend name", async () => {
    fetchMock.mockResolvedValue(json(answer([SUSPENDED])));
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    await userEvent.click(screen.getByRole("radio", { name: "Avstängda (0)" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(call(0).body).toMatchObject({ status: "Suspended", page: 1 });
    expect(await screen.findByRole("radio", { name: "Avstängda (1)" })).toHaveAttribute("aria-checked", "true");
  });
});
