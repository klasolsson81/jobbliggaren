"use client";

// "use client": the search, the filter, the sort, the pages and the panel's read run in the browser.
import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { AdminAccountsToolbar, type AdminAccountFilter } from "@/components/admin/admin-accounts-toolbar";
import { AdminAccountsTable } from "@/components/admin/admin-accounts-table";
import { AdminAccountsPager, AdminAccountsSummary } from "@/components/admin/admin-accounts-pager";
import { AdminAccountPanel, type AdminAccountDetails } from "@/components/admin/admin-account-panel";
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
} from "@/lib/admin/view-models";
import { parseRetryAfter } from "@/lib/dto/_helpers";
import {
  accountDetailsSchema,
  accountSearchResponseSchema,
  toAccountDetail,
  toAccountsPage,
} from "@/lib/dto/admin-accounts";

const SEARCH_DEBOUNCE_MS = 300;

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

/**
 * `/admin/anvandare`'s list after its first page (#1974, ADR 0151): a search that waits for typing to
 * pause, the status filter, the sort and the pages, each read through the BFF with the newest request
 * aborting the one before. The rows shown stay until newer ones arrive. Opening an account shows its
 * row at once and its details when they come.
 */
export function AccountsDirectory({ initial }: { readonly initial: AccountsListing }) {
  const t = useTranslations("admin.users");
  const [query, setQuery] = useState("");
  const [criteria, setCriteria] = useState<Criteria>(FIRST);
  const [listing, setListing] = useState(initial);
  const [answered, setAnswered] = useState<Criteria>(FIRST);
  const [open, setOpen] = useState<AdminAccountRow | null>(null);
  const [details, setDetails] = useState<AdminAccountDetails>({ kind: "loading" });
  const detailRequest = useRef<AbortController | null>(null);

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

  function openAccount(id: string) {
    const row = listing.kind === "loaded" ? listing.page.rows.find((candidate) => candidate.id === id) : undefined;
    if (row === undefined) return;
    detailRequest.current?.abort();
    const controller = new AbortController();
    detailRequest.current = controller;
    setOpen(row);
    setDetails({ kind: "loading" });
    void post("/api/admin/konton/detalj", { id }, parseDetail, controller.signal).then((answer) => {
      if (controller.signal.aborted) return;
      if (answer.ok) {
        setDetails({ kind: "loaded", data: answer.data });
        return;
      }
      setDetails({
        kind: "failed",
        message: answer.gone ? t("errors.gone") : failureText(answer.failure, t("errors.detailFailed")),
      });
      if (answer.gone) setCriteria((current) => ({ ...current, generation: current.generation + 1 }));
    });
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
      {loaded === null ? null : <AdminAccountsSummary shown={loaded.totalCount} total={loaded.counts.all} />}
      <AdminAccountsTable
        region={loaded === null ? { kind: "failed" } : listRegion(loaded.rows)}
        failedMessage={listing.kind === "failed" ? failureText(listing.failure, t("regions.failed")) : undefined}
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
        onClose={() => {
          detailRequest.current?.abort();
          setOpen(null);
        }}
      />
    </>
  );
}
