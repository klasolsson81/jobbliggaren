/**
 * Fictional data for the admin harness (#1973, ADR 0150 D5): reserved domains (RFC 2606/6761) and
 * documentation IP ranges (RFC 5737) only. The shapes follow the app's own zod schemas in
 * `src/lib/dto/me.ts` and `src/lib/dto/admin.ts`, so a page that parses them renders exactly what it
 * would render from the backend.
 */

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, "0")}`;

export const ADMIN = { userId: id(901), email: "admin@example.test", roles: ["Admin"] };
export const MEMBER = { userId: id(902), email: "medlem@example.test", roles: [] };

/** The administrator's own step-up (#1975): the challenge the code answers, and the grant it buys. */
export const STEP_UP_CHALLENGE = "admin-harness-step-up";
export const STEP_UP_GRANT = "admin-harness-grant";

/** A pending address change's two instants, as the request answers them and the read reports them. */
export const EMAIL_CHANGE_INSTANTS = {
  completableFrom: "2026-10-08T12:00:00+00:00",
  expiresAt: "2026-10-09T12:00:00+00:00",
};

export const AUDIT_PAGE = {
  items: [
    {
      id: id(1),
      occurredAt: "2026-10-03T19:05:12Z",
      correlationId: id(11),
      userId: id(902),
      impersonatedBy: null,
      eventType: "Application.StatusTransitioned",
      aggregateType: "Application",
      aggregateId: id(21),
      ipAddress: "192.0.2.0",
      userAgent: "Mozilla/5.0 (harness)",
    },
    {
      id: id(2),
      occurredAt: "2026-10-03T07:40:17Z",
      correlationId: id(12),
      userId: id(902),
      impersonatedBy: null,
      eventType: "SavedSearch.Created",
      aggregateType: "SavedSearch",
      aggregateId: id(22),
      ipAddress: "198.51.100.0",
      userAgent: "Mozilla/5.0 (harness)",
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 50,
  totalPages: 1,
};

export const RECURRING_JOBS = [
  {
    id: "sync-platsbanken-stream",
    cron: "*/10 * * * *",
    lastExecution: "2026-10-03T19:30:00Z",
    lastJobState: "Succeeded",
    nextExecution: "2026-10-03T19:40:00Z",
  },
  {
    id: "hard-delete-accounts",
    cron: "0 4 * * *",
    lastExecution: "2026-10-03T04:00:00Z",
    lastJobState: "Succeeded",
    nextExecution: "2026-10-04T04:00:00Z",
  },
];

export const FAILED_JOBS = {
  totalCount: 1,
  returned: 50,
  items: [
    {
      jobId: "18234",
      jobType: "SyncPlatsbankenStreamWorker",
      failedAt: "2026-10-03T04:15:00Z",
      errorCategory: "HttpRequestException",
    },
  ],
};

const account = (n: number, local: string, status: string, extra: Record<string, unknown> = {}) => ({
  id: id(n),
  email: `${local}@example.test`,
  role: "User",
  status,
  emailConfirmed: true,
  isSuspended: status === "Suspended",
  registeredAt: `2026-09-${String(10 + n).padStart(2, "0")}T09:00:00Z`,
  deletionEarliest: null,
  applicationCount: status === "Active" ? n : null,
  ...extra,
});

/** The account directory's answers (#1974): one search page with its counts, and one account's details. */
export const ACCOUNTS = [
  account(5, "konto.e", "Active"),
  account(4, "konto.d", "PendingDeletion", { deletionEarliest: "2026-10-30" }),
  account(3, "konto.c", "ProfileMissing", { registeredAt: null }),
  account(2, "konto.b", "Active", { emailConfirmed: false }),
  { ...account(1, "admin", "Active"), role: "Admin" },
];

/** Thirty more accounts, for a listing with a second page. */
const MORE = Array.from({ length: 30 }, (_, index) =>
  account(100 + index, `konto.extra${String(index).padStart(2, "0")}`, "Active", {
    registeredAt: "2026-08-01T09:00:00Z",
    applicationCount: 1,
  })
);

export interface AccountsQuery {
  readonly page?: number;
  readonly pageSize?: number;
  /** Thirty more accounts, so the listing has a second page. */
  readonly many?: boolean;
  /** Accounts removed since the page was read: they no longer list, and their details answer 404. */
  readonly gone?: ReadonlySet<string>;
  readonly status?: string;
  readonly access?: ReadonlyMap<string, AccountAccessState>;
}

export interface AccountAccessState {
  readonly isSuspended: boolean;
  readonly accessRevision: number;
}

function withAccess(row: (typeof ACCOUNTS)[number], access: ReadonlyMap<string, AccountAccessState>) {
  const isSuspended = access.get(row.id)?.isSuspended ?? row.isSuspended;
  const status = row.status === "ProfileMissing" || row.status === "PendingDeletion"
    ? row.status : isSuspended ? "Suspended" : "Active";
  return { ...row, status, isSuspended };
}

export function accountsPage(
  term: string | undefined,
  { page = 1, pageSize = 25, many = false, gone = new Set<string>(), status,
    access = new Map<string, AccountAccessState>() }: AccountsQuery = {}
) {
  const all = (many ? [...ACCOUNTS, ...MORE] : ACCOUNTS)
    .filter((row) => !gone.has(row.id)).map((row) => withAccess(row, access));
  const matching = term === undefined ? all : all.filter((row) => row.email.includes(term.toLowerCase()));
  const items = status === undefined ? matching : matching.filter((row) => row.status === status);
  const count = (status: string) => matching.filter((row) => row.status === status).length;
  return {
    accounts: {
      items: items.slice((page - 1) * pageSize, page * pageSize),
      totalCount: items.length,
      page,
      pageSize,
      totalPages: Math.ceil(items.length / pageSize),
    },
    counts: {
      total: matching.length,
      active: count("Active"),
      pendingDeletion: count("PendingDeletion"),
      profileMissing: count("ProfileMissing"),
      suspended: count("Suspended"),
    },
  };
}

export function accountDetails(accountId: string, gone: ReadonlySet<string> = new Set(),
  access: ReadonlyMap<string, AccountAccessState> = new Map()) {
  const row = gone.has(accountId) ? undefined : [...ACCOUNTS, ...MORE].find((candidate) => candidate.id === accountId);
  if (row === undefined) return undefined;
  const current = withAccess(row, access);
  const live = current.status === "Active" || current.status === "Suspended";
  return { ...current, resumeCount: live ? 2 : null, savedSearchCount: live ? 1 : null };
}
