import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AccountsListing } from "@/lib/admin/account-directory";
import type {
  AdminEmailChangeCancelOutcome,
  AdminEmailChangeRequestOutcome,
} from "@/lib/admin/account-email-change";
import { dismissAdminToast, getAdminToastSnapshot } from "@/lib/admin/toast-store";
import type { AdminSelf } from "@/lib/admin/view-models";
import type { CodeProof, ReauthRequestResult } from "@/lib/auth/reauth-action-state";
import { toAccountsPage, type AccountDetailsDto, type AccountSearchResponse } from "@/lib/dto/admin-accounts";

const { requestActionMock, cancelActionMock, requestReauthCodeMock } = vi.hoisted(() => ({
  requestActionMock: vi.fn<(accountId: string, newEmail: string, proof: CodeProof) => Promise<AdminEmailChangeRequestOutcome>>(),
  cancelActionMock: vi.fn<(accountId: string) => Promise<AdminEmailChangeCancelOutcome>>(),
  requestReauthCodeMock: vi.fn<() => Promise<ReauthRequestResult>>(),
}));

vi.mock("@/lib/actions/admin-accounts", () => ({
  requestAccountEmailChangeAction: requestActionMock,
  cancelAccountEmailChangeAction: cancelActionMock,
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
  requestReauthCodeMock.mockReset();
  requestReauthCodeMock.mockResolvedValue({ ok: true, challengeId: "step-up-challenge" });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  const toast = getAdminToastSnapshot();
  if (toast !== null) dismissAdminToast(toast.token);
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
    const dialog = screen.getByRole("dialog", { name: "konto.a@example.test" });
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

  it("offers the address change live, and every other action as Kommer snart (ADR 0150 D4)", async () => {
    serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: NOTHING_PENDING });
    render(<AccountsDirectory initial={FIRST} self={SELF} />);

    const dialog = await openA();

    expect(actionNames(dialog)).toEqual([
      "Agera som användaren Kommer snart",
      "Ändra e-postadress",
      "Skicka inloggningslänk Kommer snart",
      "Suspendera konto Kommer snart",
      "Radera konto Kommer snart",
      "Radera permanent Kommer snart",
    ]);
  });

  it("tells the administrator's own account by the session's id, and points it to Mina sidor", async () => {
    const own = { ...A, id: SELF.userId, email: SELF.email, role: "Admin" as const };
    serve({
      [DETAIL_ROUTE]: () => json({ ...own, resumeCount: 2, savedSearchCount: 3 }),
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

  it.each([
    ["nothing is pending", NOTHING_PENDING, "Ändra e-postadress"],
    ["a change is pending", () => json(PENDING_READ), "Avbryt adressbytet"],
  ] as const)(
    "reads the change again at the reader's request when its read failed, and moves focus to the action the answer leaves (%s)",
    async (_label, again, action) => {
      serve({ [DETAIL_ROUTE]: () => json(DETAIL), [EMAIL_CHANGE_ROUTE]: [() => json({ error: "error" }, 502), again] });
      render(<AccountsDirectory initial={FIRST} self={SELF} />);

      const panel = await openA();
      expect(actionNames(panel)).not.toContain("Ändra e-postadress");
      const retry = within(within(panel).getByRole("region", { name: "Åtgärder" })).getByRole("button", {
        name: "Försök igen",
      });
      expect(retry).toHaveAccessibleDescription("Det går inte att se om ett adressbyte väntar.");
      await userEvent.click(retry);

      await waitFor(() => expect(within(panel).getByRole("button", { name: action })).toHaveFocus());
      expect(callsTo(EMAIL_CHANGE_ROUTE)).toEqual([{ id: A.id }, { id: A.id }]);
    },
  );

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
});
