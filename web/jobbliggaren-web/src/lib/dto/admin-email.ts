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

/** Why a read has no answer. `NotConfigured` is not among them: a deployment without a provider is a state. */
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
  z.strictObject({ status: z.literal("Failed"), reason: emailAllowanceFailureSchema, ...noUsage }),
]);

const planSchema = z.discriminatedUnion("status", [
  z.strictObject({ status: z.literal("Observed"), reason: z.null(), active: emailPlanSchema, matches: z.boolean() }),
  z.strictObject({ status: z.literal("NotObserved"), reason: z.literal("NotConfigured"), active: z.null(), matches: z.null() }),
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
