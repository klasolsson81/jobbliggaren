/**
 * Fictional data for the admin harness (#1973, ADR 0150 D5): reserved domains (RFC 2606/6761) and
 * documentation IP ranges (RFC 5737) only. The shapes follow the app's own zod schemas in
 * `src/lib/dto/me.ts` and `src/lib/dto/admin.ts`, so a page that parses them renders exactly what it
 * would render from the backend.
 */

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, "0")}`;

export const ADMIN = { userId: id(901), email: "admin@example.test", roles: ["Admin"] };
export const MEMBER = { userId: id(902), email: "medlem@example.test", roles: [] };

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

export function accountsPage(term: string | undefined) {
  const items = term === undefined ? ACCOUNTS : ACCOUNTS.filter((row) => row.email.includes(term.toLowerCase()));
  const count = (status: string) => items.filter((row) => row.status === status).length;
  return {
    accounts: { items, totalCount: items.length, page: 1, pageSize: 25, totalPages: items.length === 0 ? 0 : 1 },
    counts: {
      total: items.length,
      active: count("Active"),
      pendingDeletion: count("PendingDeletion"),
      profileMissing: count("ProfileMissing"),
    },
  };
}

export function accountDetails(accountId: string) {
  const row = ACCOUNTS.find((candidate) => candidate.id === accountId);
  if (row === undefined) return undefined;
  const active = row.status === "Active";
  return { ...row, resumeCount: active ? 2 : null, savedSearchCount: active ? 1 : null };
}
