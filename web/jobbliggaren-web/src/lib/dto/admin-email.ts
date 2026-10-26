import { z } from "zod";

/**
 * `GET /api/v1/admin/email/allowance` (#1981 PR 1, ADR 0178), exactly as the API sends it: this month's mail against
 * the provider's free allowance, and the cost computed beyond it. The terms are our own configured facts and are always
 * present; the usage and the plan are closed unions that fail independently (ADR 0150 D2).
 *
 * Strict objects, like the Backup source: a key outside the contract, a state outside it or a figure where the state
 * has none fails the parse, and the region reads as failed rather than as a guess. What the page shows is formatted
 * from this, never recomputed: the backend owns the arithmetic, so the page and the overview card read one definition.
 */
const instant = z.iso.datetime({ offset: true });
const count = z.number().int().nonnegative().max(Number.MAX_SAFE_INTEGER);
const month = z.string().regex(/^\d{4}-(0[1-9]|1[0-2])$/);

export const emailPlanSchema = z.enum(["Unknown", "Essential", "Scale"]);
export type EmailPlan = z.infer<typeof emailPlanSchema>;

/**
 * Why a provider read has no answer: the allowance's, and since #1981 PR 2 delivery's and the failures list's too.
 * `NotConfigured` is not among them: a deployment without a provider is a state, and so is a missing read key.
 */
export const emailAllowanceFailureSchema = z.enum([
  "Unauthorized",
  "Forbidden",
  "RateLimited",
  "ProviderError",
  "Timeout",
  "Unreachable",
  "Malformed",
]);
export type EmailAllowanceFailure = z.infer<typeof emailAllowanceFailureSchema>;

const termsSchema = z.strictObject({
  plan: emailPlanSchema,
  includedMonthlyEmails: count,
  pricePerThousandExtra: z.number().nonnegative(),
  currency: z.literal("EUR"),
  priceReadOn: z.iso.date(),
  monthlySendLimit: count.positive(),
  costCurrency: z.literal("SEK"),
  sekPerEur: z.number().positive(),
  exchangeRateOn: z.iso.date(),
});

const noUsage = {
  observedAt: z.null(),
  month: z.null(),
  sent: z.null(),
  billable: z.null(),
  estimatedExtraCostOre: z.null(),
  aboveHalfOfSendLimit: z.null(),
  webhooksCount: z.null(),
};

const usageSchema = z.discriminatedUnion("status", [
  z.strictObject({
    status: z.literal("Observed"),
    reason: z.null(),
    observedAt: instant,
    month: month.nullable(),
    sent: count,
    billable: count.nullable(),
    estimatedExtraCostOre: count.nullable(),
    aboveHalfOfSendLimit: z.boolean(),
    webhooksCount: count.nullable(),
  }),
  z.strictObject({ status: z.literal("NotObserved"), reason: z.literal("NotConfigured"), ...noUsage }),
  // A Scaleway deployment whose read key is not installed (#1981 PR 2): an operator step owed, not a missing provider.
  z.strictObject({ status: z.literal("ReadKeyMissing"), reason: z.null(), ...noUsage }),
  z.strictObject({ status: z.literal("Failed"), reason: emailAllowanceFailureSchema, ...noUsage }),
]);

const planSchema = z.discriminatedUnion("status", [
  z.strictObject({ status: z.literal("Observed"), reason: z.null(), active: emailPlanSchema, matches: z.boolean() }),
  z.strictObject({ status: z.literal("NotObserved"), reason: z.literal("NotConfigured"), active: z.null(), matches: z.null() }),
  z.strictObject({ status: z.literal("ReadKeyMissing"), reason: z.null(), active: z.null(), matches: z.null() }),
  z.strictObject({ status: z.literal("Failed"), reason: emailAllowanceFailureSchema, active: z.null(), matches: z.null() }),
]);

export const emailAllowanceSchema = z
  .strictObject({ terms: termsSchema, usage: usageSchema, plan: planSchema })
  .superRefine((allowance, ctx) => {
    const { terms, usage, plan } = allowance;
    // The terms are the configured plan's, so they apply only to that plan; an unknown plan never matches.
    if (plan.status === "Observed" && plan.matches !== (plan.active !== "Unknown" && plan.active === terms.plan)) {
      ctx.addIssue({ code: "custom", path: ["plan", "matches"], message: "The match does not follow from the plans." });
    }
    if (usage.status !== "Observed") return;
    // A cost is computed only against the plan in force: billable and cost exist exactly when the terms apply.
    const termsApply = plan.status === "Observed" && plan.matches;
    if ((usage.billable !== null) !== termsApply || (usage.estimatedExtraCostOre !== null) !== termsApply) {
      ctx.addIssue({
        code: "custom",
        path: ["usage"],
        message: termsApply ? "An observed usage under the plan in force needs its cost." : "A cost needs the plan in force.",
      });
    }
  });
export type EmailAllowanceDto = z.infer<typeof emailAllowanceSchema>;

// ── Leverans (#1981 PR 2) ───────────────────────────────────────────────────────────────────

/** The three periods Leverans reads. The page asks for one of them and never sends a time of its own. */
export const emailDeliveryPeriodSchema = z.enum(["h24", "d3", "d7"]);
export type EmailDeliveryPeriod = z.infer<typeof emailDeliveryPeriodSchema>;

/**
 * The states a delivery read can be in, beside its answer. `NotObserved` is a deployment without Scaleway,
 * `NotObservable` a read key that sees no mail at all (so a zero could not be told from a blind key), and
 * `ReadKeyMissing` a Scaleway deployment whose read key is not installed.
 */
function unansweredDelivery<T extends z.ZodRawShape>(none: T) {
  return [
    z.strictObject({ period: emailDeliveryPeriodSchema, status: z.literal("NotObserved"), reason: z.literal("NotConfigured"), ...none }),
    z.strictObject({ period: emailDeliveryPeriodSchema, status: z.literal("NotObservable"), reason: z.null(), ...none }),
    z.strictObject({ period: emailDeliveryPeriodSchema, status: z.literal("ReadKeyMissing"), reason: z.null(), ...none }),
    z.strictObject({ period: emailDeliveryPeriodSchema, status: z.literal("Failed"), reason: emailAllowanceFailureSchema, ...none }),
  ] as const;
}

/**
 * `GET /api/v1/admin/email/delivery?period=`, exactly as the API sends it: the provider's count of sent and failed mail
 * in the period, with when we asked. The figures and that time exist exactly when the read was observed; every other
 * state carries none, so nothing here can turn an unanswered read into a zero (ADR 0150 D2).
 */
export const emailDeliverySchema = z.discriminatedUnion("status", [
  z.strictObject({
    period: emailDeliveryPeriodSchema,
    status: z.literal("Observed"),
    reason: z.null(),
    observedAt: instant,
    sent: count,
    failed: count,
  }),
  ...unansweredDelivery({ observedAt: z.null(), sent: z.null(), failed: z.null() }),
]);
export type EmailDeliveryDto = z.infer<typeof emailDeliverySchema>;

/** One reason per failed send, the most specific of the provider's flags (decided by the API, never here). */
export const emailFailureReasonSchema = z.enum([
  "MailboxNotFound",
  "HardBounce",
  "MailboxFull",
  "SoftBounce",
  "Spam",
  "Blocklisted",
  "Other",
]);
export type EmailFailureReason = z.infer<typeof emailFailureReasonSchema>;

const MASK = "•••";
const MAX_MASKED_DOMAIN_LENGTH = 253;
const MASKED_ADDRESS = /^[\p{L}\p{Nd}]?•••@([\p{L}\p{Nd}.-]+)$/u;

/**
 * A recipient in one of the three forms the API's `MaskedEmailAddress` makes, and nothing else: the local part's first
 * code point when it is a letter or a decimal digit, then the mask, `@` and the whole domain; the mask, `@` and the
 * domain when that code point is anything else; or the mask alone. The domain holds only letters, digits, `-` and `.`,
 * at most 253 UTF-16 units. An address that was not masked fails the parse, so the page can never show it.
 */
function isMaskedRecipient(value: string): boolean {
  if (value === MASK) return true;
  const domain = MASKED_ADDRESS.exec(value)?.[1];
  return domain !== undefined && domain.length <= MAX_MASKED_DOMAIN_LENGTH;
}

const failureRowSchema = z.strictObject({
  createdAt: instant,
  recipient: z.string().max(320).refine(isMaskedRecipient, { message: "The recipient is not masked." }),
  reason: emailFailureReasonSchema,
});
export type EmailFailureRow = z.infer<typeof failureRowSchema>;

/** The most rows one failures read carries; the API reads one page of this size and never pages. */
export const EMAIL_FAILURES_LIMIT = 10;

/**
 * `GET /api/v1/admin/email/failures?period=`, exactly as the API sends it: the latest failed sends in the period with
 * the time, the masked recipient and one closed reason, and nothing else. The rows exist exactly when the
 * read was observed, and an observed empty list is a real empty; every other state carries none.
 */
export const emailFailuresSchema = z
  .discriminatedUnion("status", [
    z.strictObject({
      period: emailDeliveryPeriodSchema,
      status: z.literal("Observed"),
      reason: z.null(),
      observedAt: instant,
      failures: z.array(failureRowSchema),
      limit: z.literal(EMAIL_FAILURES_LIMIT),
    }),
    ...unansweredDelivery({ observedAt: z.null(), failures: z.null(), limit: z.literal(EMAIL_FAILURES_LIMIT) }),
  ])
  .superRefine((read, ctx) => {
    if (read.status !== "Observed") return;
    if (read.failures.length > read.limit) {
      ctx.addIssue({ code: "custom", path: ["failures"], message: "More rows than the read's limit." });
    }
    // The list is the newest first; a list in another order is not the list the page says it shows.
    read.failures.forEach((row, index) => {
      const newer = read.failures[index - 1];
      if (newer !== undefined && Date.parse(newer.createdAt) < Date.parse(row.createdAt)) {
        ctx.addIssue({ code: "custom", path: ["failures", index, "createdAt"], message: "The rows are not newest first." });
      }
    });
  });
export type EmailFailuresDto = z.infer<typeof emailFailuresSchema>;

// ── Utskick per mejltyp (#1981 PR 3) ────────────────────────────────────────────────────────

/** The seven mail kinds, in the order the API sends them and the page lists them. */
export const EMAIL_KINDS = [
  "login-challenge",
  "account-email-change-requested-notification",
  "email-changed-notification",
  "match-notification",
  "followed-company-notification",
  "feedback-received-notification",
  "feedback-reply",
] as const;
export const emailKindSchema = z.enum(EMAIL_KINDS);
export type EmailKind = z.infer<typeof emailKindSchema>;

/** What a failed handover to the provider came to, in the order a day's classes are listed: a display order only. */
export const EMAIL_SEND_FAILURES = ["Refused", "RateLimited", "Unreachable", "ProviderError", "Timeout", "Other"] as const;
export const emailSendFailureSchema = z.enum(EMAIL_SEND_FAILURES);
export type EmailSendFailure = z.infer<typeof emailSendFailureSchema>;

const day = z.iso.date();

const kindCountSchema = z.strictObject({
  kind: emailKindSchema,
  accepted: count,
  failed: count,
  latestFailure: z.strictObject({ day, outcomes: z.array(emailSendFailureSchema).min(1) }).nullable(),
});
export type EmailKindCount = z.infer<typeof kindCountSchema>;

/**
 * `GET /api/v1/admin/email/kinds?period=`, exactly as the API sends it: our own count of handovers to the provider per
 * mail kind in the period. `from` is the period's first day; `coverage` says whether our count covers the whole period
 * or began inside it, and `coveredFrom` is when it began. `NotObserved` is a deployment without Scaleway, and
 * `NotObservable` a count with no sends in a period the provider sent mail in, so a zero could not be told from a
 * count that stopped (ADR 0150 D2). The counts exist exactly when the read was observed.
 */
export const emailKindsSchema = z
  .discriminatedUnion("status", [
    z.strictObject({
      period: emailDeliveryPeriodSchema,
      status: z.literal("Observed"),
      from: day,
      coverage: z.enum(["Complete", "Partial"]),
      coveredFrom: instant.nullable(),
      kinds: z.array(kindCountSchema),
    }),
    z.strictObject({
      period: emailDeliveryPeriodSchema,
      status: z.literal("NotObserved"),
      from: z.null(),
      coverage: z.null(),
      coveredFrom: z.null(),
      kinds: z.null(),
    }),
    z.strictObject({
      period: emailDeliveryPeriodSchema,
      status: z.literal("NotObservable"),
      from: day,
      coverage: z.null(),
      coveredFrom: z.null(),
      kinds: z.null(),
    }),
  ])
  .superRefine((read, ctx) => {
    if (read.status !== "Observed") return;
    if ((read.coveredFrom !== null) !== (read.coverage === "Partial")) {
      ctx.addIssue({ code: "custom", path: ["coveredFrom"], message: "A start exists exactly when the count began inside the period." });
    }
    // Every kind once, in the API's order: a missing kind would read as one that sent nothing.
    if (read.kinds.length !== EMAIL_KINDS.length || read.kinds.some((row, index) => row.kind !== EMAIL_KINDS[index])) {
      ctx.addIssue({ code: "custom", path: ["kinds"], message: "Not the seven kinds in their order." });
    }
    read.kinds.forEach((row, index) => {
      if ((row.failed === 0) !== (row.latestFailure === null)) {
        ctx.addIssue({ code: "custom", path: ["kinds", index, "latestFailure"], message: "A latest failure exists exactly when one failed." });
      }
      const order = row.latestFailure?.outcomes.map((outcome) => EMAIL_SEND_FAILURES.indexOf(outcome)) ?? [];
      if (order.some((position, at) => at > 0 && position <= (order[at - 1] ?? -1))) {
        ctx.addIssue({ code: "custom", path: ["kinds", index, "latestFailure", "outcomes"], message: "The classes are not distinct and in order." });
      }
    });
  });
export type EmailKindsDto = z.infer<typeof emailKindsSchema>;

// ── DMARC-rapporter (#1981 PR 4b, ADR 0185) ─────────────────────────────────────────────────

/** The most sources one read carries (`DmarcReportsEvaluator.MaxSources`); the rest are counted, not listed. */
export const DMARC_MAX_SOURCES = 25;

/** The region's own fixed window (`DmarcReportsEvaluator.Window`). The copy names it, so a read of another fails. */
export const DMARC_WINDOW_MS = 7 * 24 * 60 * 60 * 1000;

/** How a latest run that tried to read the mailbox ended, when it did not succeed. */
export const DMARC_LATEST_FAILURES = [
  "Unreachable",
  "TlsFailed",
  "AuthRefused",
  "Timeout",
  "Protocol",
  "Incomplete",
  "AuthUnconfirmed",
] as const;
export const dmarcLatestFailureSchema = z.enum(DMARC_LATEST_FAILURES);
export type DmarcLatestFailure = z.infer<typeof dmarcLatestFailureSchema>;

/**
 * The address as the backend canonicalises it. Only its alphabet is checked, never its grammar: an IPv6 address can carry
 * a dotted IPv4 tail, and a check stricter than the backend's would let one sender's report blank the whole region.
 */
const dmarcAddress = z.string().regex(/^[0-9a-f:.]{1,45}$/, { message: "Not an address in canonical form." });

/** The header-from domain as acceptance keeps it (`DmarcAggregateReport.HeaderFromOf`): lower-case LDH labels under the domain. */
const dmarcHeaderFrom = z
  .string()
  .max(253)
  .regex(/^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)*jobbliggaren\.se$/, { message: "Not a header-from domain acceptance keeps." });

const dmarcIngestionSchema = z
  .strictObject({
    lastAttemptAt: instant,
    lastSuccessAt: instant.nullable(),
    latestFailure: dmarcLatestFailureSchema.nullable(),
    signInResumesAt: instant.nullable(),
    stale: z.boolean(),
  })
  .superRefine((ingestion, ctx) => {
    if (ingestion.latestFailure === null && ingestion.lastSuccessAt === null) {
      ctx.addIssue({ code: "custom", path: ["lastSuccessAt"], message: "A latest run that succeeded is the latest success." });
    }
    if (ingestion.latestFailure === "Incomplete" && ingestion.lastSuccessAt !== null) {
      ctx.addIssue({ code: "custom", path: ["latestFailure"], message: "Incomplete comes only before any success." });
    }
    if (ingestion.latestFailure !== null && !ingestion.stale) {
      ctx.addIssue({ code: "custom", path: ["stale"], message: "A mailbox whose latest run failed is stale." });
    }
    // Which outcomes back off is the backend's rule; the browser only checks that a resume follows a failed attempt.
    if (
      ingestion.signInResumesAt !== null &&
      (ingestion.latestFailure === null || Date.parse(ingestion.signInResumesAt) <= Date.parse(ingestion.lastAttemptAt))
    ) {
      ctx.addIssue({ code: "custom", path: ["signInResumesAt"], message: "A resume follows a failed attempt." });
    }
  });
export type DmarcIngestion = z.infer<typeof dmarcIngestionSchema>;

const dmarcMailboxSchema = z.strictObject({
  skipped: count,
  rejected: count,
  volumeCapped: count,
  abandoned: count,
  retryPending: count,
});
export type DmarcMailboxCounts = z.infer<typeof dmarcMailboxSchema>;

const dmarcSourceSchema = z
  .strictObject({
    address: dmarcAddress,
    headerFrom: dmarcHeaderFrom,
    messages: count.positive(),
    dkimAligned: count,
    spfAligned: count,
    failingDmarc: count,
    flagged: z.boolean(),
  })
  .superRefine((source, ctx) => {
    if (source.dkimAligned > source.messages || source.spfAligned > source.messages || source.failingDmarc > source.messages) {
      ctx.addIssue({ code: "custom", message: "Each figure is a part of the source's messages." });
    }
    if (source.flagged !== source.failingDmarc > 0) {
      ctx.addIssue({ code: "custom", path: ["flagged"], message: "A source is flagged exactly when a message failed." });
    }
  });
export type DmarcSource = z.infer<typeof dmarcSourceSchema>;

const noDmarcFigures = {
  coveredFrom: z.null(),
  coveredTo: z.null(),
  reports: z.null(),
  reporters: z.null(),
  messages: z.null(),
  failingMessages: z.null(),
  sourceCount: z.null(),
  sources: z.null(),
};

const noDmarcRead = {
  ingestion: z.null(),
  from: z.null(),
  to: z.null(),
  conflictingReports: z.null(),
  mailbox: z.null(),
  ...noDmarcFigures,
};

/**
 * `GET /api/v1/admin/email/dmarc`, exactly as the API sends it (ADR 0185 D9): every key is always present, and a value a
 * state has none of is null. The ingestion state once a run has read the mailbox; the window and the mailbox counts once
 * one has succeeded; figures only when a report is in the window. The figures are the reporters' own statements.
 */
export const emailDmarcSchema = z
  .discriminatedUnion("status", [
    z.strictObject({
      status: z.literal("Observed"),
      ingestion: dmarcIngestionSchema,
      from: instant,
      to: instant,
      coveredFrom: instant,
      coveredTo: instant,
      conflictingReports: count,
      mailbox: dmarcMailboxSchema,
      reports: count.positive(),
      reporters: count.positive(),
      messages: count.positive(),
      failingMessages: count,
      sourceCount: count.positive(),
      sources: z.array(dmarcSourceSchema).max(DMARC_MAX_SOURCES),
    }),
    z.strictObject({
      status: z.literal("NoReports"),
      ingestion: dmarcIngestionSchema,
      from: instant,
      to: instant,
      conflictingReports: count,
      mailbox: dmarcMailboxSchema,
      ...noDmarcFigures,
    }),
    z.strictObject({ status: z.literal("Failed"), ...noDmarcRead, ingestion: dmarcIngestionSchema }),
    z.strictObject({ status: z.literal("NotObservable"), ...noDmarcRead }),
    z.strictObject({ status: z.literal("NotConfigured"), ...noDmarcRead }),
    z.strictObject({ status: z.literal("Misconfigured"), ...noDmarcRead }),
  ])
  .superRefine((read, ctx) => {
    if (read.status === "Failed" && read.ingestion.lastSuccessAt !== null) {
      ctx.addIssue({ code: "custom", path: ["ingestion", "lastSuccessAt"], message: "A mailbox read once is not failed." });
    }
    if (read.status !== "Observed" && read.status !== "NoReports") return;
    if (read.ingestion.lastSuccessAt === null) {
      ctx.addIssue({ code: "custom", path: ["ingestion", "lastSuccessAt"], message: "A window follows a successful run." });
    }
    if (Date.parse(read.to) - Date.parse(read.from) !== DMARC_WINDOW_MS) {
      ctx.addIssue({ code: "custom", path: ["from"], message: "Not the region's seven-day window." });
    }
    if (read.status !== "Observed") return;
    if (read.reporters > read.reports || read.failingMessages > read.messages) {
      ctx.addIssue({ code: "custom", message: "No more reporters than reports, and no more failing than counted." });
    }
    const from = Date.parse(read.from);
    const to = Date.parse(read.to);
    const coveredFrom = Date.parse(read.coveredFrom);
    const coveredTo = Date.parse(read.coveredTo);
    if (coveredFrom >= coveredTo || coveredFrom >= to || coveredTo <= from) {
      ctx.addIssue({ code: "custom", path: ["coveredFrom"], message: "The covered period overlaps the window." });
    }
    if (read.sources.length !== Math.min(read.sourceCount, DMARC_MAX_SOURCES)) {
      ctx.addIssue({ code: "custom", path: ["sources"], message: "The sources shown are every source, up to the cap." });
    }
    const shownMessages = read.sources.reduce((sum, source) => sum + source.messages, 0);
    const shownFailing = read.sources.reduce((sum, source) => sum + source.failingDmarc, 0);
    const allShown = read.sources.length === read.sourceCount;
    if (
      shownMessages > read.messages ||
      shownFailing > read.failingMessages ||
      (allShown && (shownMessages !== read.messages || shownFailing !== read.failingMessages))
    ) {
      ctx.addIssue({ code: "custom", path: ["sources"], message: "The totals are the sums over every source." });
    }
    const pairs = new Set<string>();
    read.sources.forEach((source, index) => {
      const pair = `${source.address} ${source.headerFrom}`;
      if (pairs.has(pair)) {
        ctx.addIssue({ code: "custom", path: ["sources", index], message: "A source and domain appear once." });
      }
      pairs.add(pair);
      // Flagged first, then by failing and by messages: the order the page says it shows.
      const before = read.sources[index - 1];
      if (before !== undefined && compareDmarcSources(before, source) > 0) {
        ctx.addIssue({ code: "custom", path: ["sources", index], message: "The sources are not in their order." });
      }
    });
  });
export type EmailDmarcDto = z.infer<typeof emailDmarcSchema>;

function compareDmarcSources(a: DmarcSource, b: DmarcSource): number {
  return Number(b.flagged) - Number(a.flagged) || b.failingDmarc - a.failingDmarc || b.messages - a.messages;
}
