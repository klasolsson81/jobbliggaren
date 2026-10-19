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

/**
 * A recipient as the API masked it: one character, the mask and the domain, or the mask alone. Anything else, an
 * address that was not masked above all, fails the parse, so the page can never show it. The one character is a whole
 * text element, as the API cuts it; only the server parses a failures read, so the segmenter is made only there.
 */
function isMaskedRecipient(value: string): boolean {
  if (value === MASK) return true;
  const at = value.indexOf(`${MASK}@`);
  if (at <= 0) return false;
  const first = value.slice(0, at);
  const domain = value.slice(at + MASK.length + 1);
  const elements = [...new Intl.Segmenter("sv", { granularity: "grapheme" }).segment(first)].length;
  return elements === 1 && !/[\s@•]/u.test(first) && /^[^\s@]+$/u.test(domain);
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
