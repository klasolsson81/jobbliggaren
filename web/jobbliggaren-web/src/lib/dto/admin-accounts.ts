import { z } from "zod";
import type { AdminPendingEmailChange } from "@/lib/admin/account-email-change";
import type {
  AdminAccountDetail,
  AdminAccountRow,
  AdminAccountStatus,
} from "@/lib/admin/view-models";
import { pagedResultWithTotalPages, readableInstantSchema as instant } from "./_helpers";

/**
 * The admin account directory's wire shapes (#1974, ADR 0151). The backend names its enums the .NET way;
 * this file is the one place they become the view model's. `Suspended` is read before #1976 sends it, so an
 * API that learns it first does not break a web that has not.
 */
const accountStatusSchema = z.enum(["Active", "PendingDeletion", "ProfileMissing", "Suspended"]);
const accountRoleSchema = z.enum(["User", "Admin"]);
const count = z.number().int().nonnegative();

const deletionTimingSchema = z.object({
  deletedAt: instant,
  eligibleAt: instant,
  scheduledRunAt: instant,
}).refine((value) => Date.parse(value.eligibleAt) > Date.parse(value.deletedAt)
  && Date.parse(value.scheduledRunAt) > Date.parse(value.eligibleAt));

export const accountDeletionReceiptSchema = deletionTimingSchema.safeExtend({ userId: z.guid() });

export const accountAccessReceiptSchema = z.object({
  userId: z.guid(),
  isSuspended: z.boolean(),
  accessRevision: z.number().int().positive().max(Number.MAX_SAFE_INTEGER),
  pendingDeletion: z.boolean(),
});

const accountListItemSchema = z.object({
  id: z.guid(),
  email: z.string().nullable(),
  role: accountRoleSchema,
  status: accountStatusSchema,
  emailConfirmed: z.boolean(),
  isSuspended: z.boolean(),
  registeredAt: z.string().nullable(),
  deletionEarliest: z.string().regex(/^\d{4}-\d{2}-\d{2}$/).nullable(),
  deletion: deletionTimingSchema.nullable().default(null),
  applicationCount: count.nullable(),
});

export const accountDetailsSchema = accountListItemSchema.extend({
  resumeCount: count.nullable(),
  savedSearchCount: count.nullable(),
  deletionPreview: deletionTimingSchema.nullable().default(null),
});

export const accountStatusCountsSchema = z.object({
  total: count,
  active: count,
  pendingDeletion: count,
  profileMissing: count,
  suspended: count,
});

export const accountSearchResponseSchema = z.object({
  accounts: pagedResultWithTotalPages(accountListItemSchema),
  counts: accountStatusCountsSchema,
});

export type AccountSearchResponse = z.infer<typeof accountSearchResponseSchema>;
export type AccountDetailsDto = z.infer<typeof accountDetailsSchema>;
export type AccountStatusCountsDto = z.infer<typeof accountStatusCountsSchema>;

/** The sort orders the directory knows; each ends on the account id. */
export const ACCOUNT_SORTS = ["RegisteredNewest", "RegisteredOldest", "AddressAscending", "AddressDescending"] as const;
export type AccountSort = (typeof ACCOUNT_SORTS)[number];

/** The statuses a search can filter by: the ones the backend produces. */
export const ACCOUNT_SEARCH_STATUSES = ["Active", "PendingDeletion", "ProfileMissing", "Suspended"] as const;
export type AccountSearchStatus = (typeof ACCOUNT_SEARCH_STATUSES)[number];

/** A search request: the term travels only in a POST body, never in a URL. */
export interface AccountSearchCriteria {
  readonly address?: string;
  readonly registeredFrom?: string;
  readonly registeredBefore?: string;
  readonly status?: AccountSearchStatus;
  readonly sort: AccountSort;
  readonly page: number;
  readonly pageSize: number;
}

const STATUS: Readonly<Record<z.infer<typeof accountStatusSchema>, AdminAccountStatus>> = {
  Active: "active",
  PendingDeletion: "pendingDeletion",
  ProfileMissing: "profileMissing",
  Suspended: "suspended",
};

export function toAccountRow(item: z.infer<typeof accountListItemSchema>): AdminAccountRow {
  return {
    id: item.id,
    email: item.email,
    role: item.role === "Admin" ? "admin" : "user",
    status: STATUS[item.status],
    emailConfirmed: item.emailConfirmed,
    isSuspended: item.isSuspended,
    registeredAt: item.registeredAt,
    applicationCount: item.applicationCount,
    deletionEarliest: item.deletionEarliest,
    deletion: item.deletion,
  };
}

export function toAccountDetail(item: AccountDetailsDto): AdminAccountDetail {
  return { ...toAccountRow(item), resumeCount: item.resumeCount, savedSearchCount: item.savedSearchCount,
    deletionPreview: item.deletionPreview };
}

/** The filters the directory counts, keyed as the toolbar names them. */
export type AccountCountKey = "all" | "active" | "pendingDeletion" | "profileMissing" | "suspended";

/** One answered search as the account page shows it. */
export interface AccountsPage {
  readonly rows: ReadonlyArray<AdminAccountRow>;
  readonly page: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly counts: Readonly<Record<AccountCountKey, number>>;
}

/**
 * `GET /api/v1/admin/accounts/{id}/email-change` → 200 (#1975, ADR 0153); a 204 means nothing is pending.
 * No address: the read carries none.
 */
export const pendingEmailChangeSchema = z.object({
  state: z.enum(["Pending", "CodeBurned"]),
  completableFrom: instant,
  expiresAt: instant,
});

export type PendingEmailChangeDto = z.infer<typeof pendingEmailChangeSchema>;

/** `POST /api/v1/admin/accounts/{id}/email-change` → 202: the change is pending from now, with its two instants. */
export const emailChangeRequestedSchema = z.object({
  completableFrom: instant,
  expiresAt: instant,
});

/** The account BFF's answer for the panel's read: the pending change, or null when there is none. */
export const pendingEmailChangeReadSchema = z.object({
  pending: pendingEmailChangeSchema.nullable(),
});

export function toPendingEmailChange(dto: PendingEmailChangeDto): AdminPendingEmailChange {
  return {
    state: dto.state === "CodeBurned" ? "codeBurned" : "pending",
    completableFrom: dto.completableFrom,
    expiresAt: dto.expiresAt,
  };
}

export function toAccountsPage(response: AccountSearchResponse): AccountsPage {
  const { accounts, counts } = response;
  return {
    rows: accounts.items.map(toAccountRow),
    page: accounts.page,
    totalCount: accounts.totalCount,
    totalPages: accounts.totalPages,
    counts: {
      all: counts.total,
      active: counts.active,
      pendingDeletion: counts.pendingDeletion,
      profileMissing: counts.profileMissing,
      suspended: counts.suspended,
    },
  };
}
