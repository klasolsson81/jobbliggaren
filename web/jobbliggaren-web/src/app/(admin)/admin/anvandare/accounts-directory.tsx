"use client";

// "use client": the search, the filter, the sort, the pages and the panel's reads and commands run in the browser.
import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { STANDALONE_LINK } from "@/components/auth/mail-link";
import { AdminAccountsToolbar, type AdminAccountFilter } from "@/components/admin/admin-accounts-toolbar";
import { AdminAccountsTable } from "@/components/admin/admin-accounts-table";
import { AdminAccountsPager, AdminAccountsSummary } from "@/components/admin/admin-accounts-pager";
import {
  AdminAccountPanel,
  type AdminAccountCommands,
  type AdminAccountDetails,
  type AdminLiveAction,
} from "@/components/admin/admin-account-panel";
import { cancelAccountEmailChangeAction, requestAccountEmailChangeAction } from "@/lib/actions/admin-accounts";
import type { AdminEmailChangeState } from "@/lib/admin/account-email-change";
import {
  ACCOUNTS_PAGE_SIZE,
  FIRST_SORT,
  wireSort,
  wireStatus,
  type AccountsFailure,
  type AccountsListing,
} from "@/lib/admin/account-directory";
import {
  listRegion,
  type AdminAccountRow,
  type AdminAccountSort,
  type AdminAccountSortKey,
  type AdminSelf,
} from "@/lib/admin/view-models";
import { requestReauthCode } from "@/lib/auth/reauth-actions";
import { parseRetryAfter } from "@/lib/dto/_helpers";
import {
  accountDetailsSchema,
  accountSearchResponseSchema,
  pendingEmailChangeReadSchema,
  toAccountDetail,
  toAccountsPage,
  toPendingEmailChange,
} from "@/lib/dto/admin-accounts";

const SEARCH_DEBOUNCE_MS = 300;

/** ADR 0150 D4: the address change and its cancel are built (#1975); every other action is "Kommer snart". */
const LIVE: ReadonlySet<AdminLiveAction> = new Set(["changeEmail", "cancelEmailChange"]);

const RETURN_PATH = "/admin/anvandare";

interface Criteria {
  readonly term: string;
  readonly filter: AdminAccountFilter;
  readonly sort: AdminAccountSort;
  readonly page: number;
  /** Bumped to read the same criteria again. */
  readonly generation: number;
}

const FIRST: Criteria = { term: "", filter: "all", sort: FIRST_SORT, page: 1, generation: 0 };

type Answer<T> = { readonly ok: true; readonly data: T } | { readonly ok: false; readonly failure: AccountsFailure; readonly gone: boolean };

/** One BFF read. The body carries every value, so no term and no account id enters a URL. */
async function post<T>(path: string, body: unknown, parse: (json: unknown) => T | null, signal: AbortSignal): Promise<Answer<T>> {
  try {
    const response = await fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      cache: "no-store",
      signal,
    });
    if (response.ok) {
      const data = parse(await response.json());
      return data === null ? { ok: false, failure: { reason: "error" }, gone: false } : { ok: true, data };
    }
    switch (response.status) {
      case 401:
        return { ok: false, failure: { reason: "unauthorized" }, gone: false };
      case 403:
        return { ok: false, failure: { reason: "forbidden" }, gone: false };
      case 404:
        return { ok: false, failure: { reason: "error" }, gone: true };
      case 429:
        return {
          ok: false,
          failure: { reason: "rateLimited", retryAfterSeconds: parseRetryAfter(response.headers.get("Retry-After")) },
          gone: false,
        };
      default:
        return { ok: false, failure: { reason: "error" }, gone: false };
    }
  } catch {
    return { ok: false, failure: { reason: "error" }, gone: false };
  }
}

const parsePage = (json: unknown) => {
  const parsed = accountSearchResponseSchema.safeParse(json);
  return parsed.success ? toAccountsPage(parsed.data) : null;
};

const parseDetail = (json: unknown) => {
  const parsed = accountDetailsSchema.safeParse(json);
  return parsed.success ? toAccountDetail(parsed.data) : null;
};

const parseEmailChange = (json: unknown): AdminEmailChangeState | null => {
  const parsed = pendingEmailChangeReadSchema.safeParse(json);
  if (!parsed.success) return null;
  return parsed.data.pending === null
    ? { kind: "none" }
    : { kind: "pending", change: toPendingEmailChange(parsed.data.pending) };
};

const UNKNOWN_EMAIL_CHANGE: AdminEmailChangeState = { kind: "unknown" };

/**
 * `/admin/anvandare`'s list after its first page (#1974, ADR 0151): a search that waits for typing to
 * pause, the status filter, the sort and the pages, each read through the BFF with the newest request
 * aborting the one before. The rows shown stay until newer ones arrive. Opening an account shows its
 * row at once and its details when they come, its pending address change read beside them (#1975), so a
 * fault on the volatile instance costs that one fact.
 */
export function AccountsDirectory({ initial, self }: { readonly initial: AccountsListing; readonly self: AdminSelf }) {
  const t = useTranslations("admin.users");
  const [query, setQuery] = useState("");
  const [criteria, setCriteria] = useState<Criteria>(FIRST);
  const [listing, setListing] = useState(initial);
  const [answered, setAnswered] = useState<Criteria>(FIRST);
  const [open, setOpen] = useState<AdminAccountRow | null>(null);
  const [details, setDetails] = useState<AdminAccountDetails>({ kind: "loading" });
  const [emailChange, setEmailChange] = useState<AdminEmailChangeState>({ kind: "none" });
  const detailRequest = useRef<AbortController | null>(null);
  const openId = useRef<string | null>(null);
  const tableRegion = useRef<HTMLDivElement>(null);
  const refocusTableAfterRetry = useRef(false);

  // The term follows the field once typing pauses, and a new term starts again from the first page.
  useEffect(() => {
    const timer = setTimeout(() => {
      const term = query.trim();
      setCriteria((current) => (current.term === term ? current : { ...current, term, page: 1 }));
    }, SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [query]);

  useEffect(() => {
    if (criteria === answered) return;
    const controller = new AbortController();
    void post(
      "/api/admin/konton",
      {
        address: criteria.term === "" ? undefined : criteria.term,
        status: wireStatus(criteria.filter),
        sort: wireSort(criteria.sort),
        page: criteria.page,
        pageSize: ACCOUNTS_PAGE_SIZE,
      },
      parsePage,
      controller.signal,
    ).then((answer) => {
      if (controller.signal.aborted) return;
      setListing(answer.ok ? { kind: "loaded", page: answer.data } : { kind: "failed", failure: answer.failure });
      setAnswered(criteria);
    });
    return () => controller.abort();
  }, [criteria, answered]);

  useEffect(() => () => detailRequest.current?.abort(), []);

  // The retry button leaves with the failed line once rows arrive, and the browser drops its focus.
  useEffect(() => {
    if (!refocusTableAfterRetry.current) return;
    refocusTableAfterRetry.current = false;
    const active = document.activeElement;
    if (listing.kind === "loaded" && (active === null || active === document.body)) tableRegion.current?.focus();
  }, [listing]);

  function failureText(failure: AccountsFailure, fallback: string): string {
    switch (failure.reason) {
      case "rateLimited":
        return t("errors.rateLimited", { seconds: failure.retryAfterSeconds });
      case "unauthorized":
        return t("errors.unauthorized");
      case "forbidden":
        return t("errors.forbidden");
      case "error":
        return fallback;
    }
  }

  function recoveryOf(failure: AccountsFailure): "retry" | "signIn" | "none" {
    if (failure.reason === "unauthorized") return "signIn";
    return failure.reason === "forbidden" ? "none" : "retry";
  }

  function markGone() {
    setDetails({ kind: "gone" });
    setCriteria((current) => ({ ...current, generation: current.generation + 1 }));
  }

  /**
   * Reads one account's details and its pending address change, side by side, and shows them together. Again,
   * after a command, it keeps what the panel shows until the answers arrive, and a failed read changes nothing.
   */
  function readAccount(id: string, again = false) {
    detailRequest.current?.abort();
    const controller = new AbortController();
    detailRequest.current = controller;
    if (!again) setDetails({ kind: "loading" });
    const detail = post("/api/admin/konton/detalj", { id }, parseDetail, controller.signal);
    const change = post("/api/admin/konton/adressbyte", { id }, parseEmailChange, controller.signal);
    void Promise.all([detail, change]).then(([answer, pending]) => {
      if (controller.signal.aborted) return;
      if (pending.ok || !again) setEmailChange(pending.ok ? pending.data : UNKNOWN_EMAIL_CHANGE);
      if (answer.ok) {
        setDetails({ kind: "loaded", data: answer.data });
        return;
      }
      if (answer.gone) {
        markGone();
        return;
      }
      if (again) return;
      setDetails({
        kind: "failed",
        message: failureText(answer.failure, t("errors.detailFailed")),
        recovery: recoveryOf(answer.failure),
      });
    });
  }

  function openAccount(id: string) {
    const row = listing.kind === "loaded" ? listing.page.rows.find((candidate) => candidate.id === id) : undefined;
    if (row === undefined) return;
    openId.current = row.id;
    setOpen(row);
    setEmailChange({ kind: "none" });
    readAccount(row.id);
  }

  /** A command's answer for the account still open; one for an account since closed is dropped. */
  function stillOpen(id: string): boolean {
    return openId.current === id;
  }

  // The panel's commands (ADR 0150 D4): the address change and its cancel through their Server Actions. What a
  // request or a cancel answers becomes the panel's pending change, never what the form held.
  const commands: AdminAccountCommands = {
    live: LIVE,
    emailChange: {
      requestCode: requestReauthCode,
      request: async (account, newEmail, proof) => {
        const outcome = await requestAccountEmailChangeAction(account.id, newEmail, proof);
        if (!stillOpen(account.id)) return outcome;
        if (outcome.ok) setEmailChange({ kind: "pending", change: outcome.value });
        else if (outcome.kind === "outcomeUnknown") setEmailChange(UNKNOWN_EMAIL_CHANGE);
        if (outcome.after === "gone") markGone();
        else if (outcome.after !== undefined) readAccount(account.id, true);
        return outcome;
      },
      cancel: async (account) => {
        const outcome = await cancelAccountEmailChangeAction(account.id);
        if (!stillOpen(account.id)) return outcome;
        if (outcome.kind === "cancelled") setEmailChange({ kind: "none" });
        else if (outcome.kind === "unknown") setEmailChange(UNKNOWN_EMAIL_CHANGE);
        else if (outcome.kind === "nothingPending") readAccount(account.id, true);
        return outcome;
      },
      returnPath: RETURN_PATH,
    },
  };

  function retryListing() {
    refocusTableAfterRetry.current = true;
    setCriteria((current) => ({ ...current, generation: current.generation + 1 }));
  }

  function failedAction(failure: AccountsFailure) {
    switch (recoveryOf(failure)) {
      case "retry":
        return (
          <button type="button" className="jp-btn jp-btn--secondary jp-btn--sm" onClick={retryListing}>
            {t("errors.retry")}
          </button>
        );
      case "signIn":
        return (
          <Link href="/logga-in" className={STANDALONE_LINK}>
            {t("errors.signIn")}
          </Link>
        );
      case "none":
        return null;
    }
  }

  function toggleSort(key: AdminAccountSortKey) {
    setCriteria((current) => ({
      ...current,
      page: 1,
      sort:
        current.sort.key === key
          ? { key, direction: current.sort.direction === "ascending" ? "descending" : "ascending" }
          : { key, direction: key === "email" ? "ascending" : "descending" },
    }));
  }

  const loaded = listing.kind === "loaded" ? listing.page : null;

  return (
    <>
      <AdminAccountsToolbar
        filter={criteria.filter}
        query={query}
        onQueryChange={setQuery}
        onFilterChange={(filter) => setCriteria((current) => ({ ...current, filter, page: 1 }))}
        counts={loaded?.counts}
      />
      {loaded === null ? null : (
        <AdminAccountsSummary shown={loaded.totalCount} total={loaded.counts.all} busy={criteria !== answered} />
      )}
      <AdminAccountsTable
        region={loaded === null ? { kind: "failed" } : listRegion(loaded.rows)}
        failedMessage={listing.kind === "failed" ? failureText(listing.failure, t("regions.failed")) : undefined}
        failedAction={listing.kind === "failed" ? failedAction(listing.failure) : undefined}
        scrollRef={tableRegion}
        busy={criteria !== answered}
        sort={criteria.sort}
        onSort={toggleSort}
        selectedId={open?.id ?? null}
        onOpen={openAccount}
      />
      {loaded === null ? null : (
        <AdminAccountsPager
          page={loaded.page}
          pages={loaded.totalPages}
          onPage={(page) => setCriteria((current) => ({ ...current, page }))}
        />
      )}
      <AdminAccountPanel
        account={open}
        details={details}
        commands={commands}
        self={self}
        emailChange={emailChange}
        onRetry={open === null ? undefined : () => readAccount(open.id)}
        fallbackFocus={() => tableRegion.current}
        onClose={() => {
          detailRequest.current?.abort();
          openId.current = null;
          setOpen(null);
        }}
      />
    </>
  );
}
