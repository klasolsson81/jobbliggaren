import type { EmailAllowanceDto, EmailAllowanceFailure } from "@/lib/dto/admin-email";

/**
 * What the e-post page and the overview's e-post card read from the allowance (#1981 PR 1, ADR 0178). Both format the
 * same three figures, so both take them from here: a figure is a number, or null when it is unknown, and an unknown
 * figure is never 0 (ADR 0150 D2).
 */
export interface AllowanceFigures {
  /** The provider's count of mail sent this month in the project. */
  readonly sent: number | null;
  /** The configured plan's free mails, shown only while the provider says that plan is in force. */
  readonly included: number | null;
  /** The computed cost beyond the allowance, in whole öre, Swedish VAT included. A computed 0 is a value. */
  readonly costOre: number | null;
}

export function allowanceFigures(allowance: EmailAllowanceDto): AllowanceFigures {
  const { usage, plan, terms } = allowance;
  // A deployment with no provider to ask has no figure at all, the configured terms included.
  if (usage.status === "NotObserved") return { sent: null, included: null, costOre: null };
  return {
    sent: usage.status === "Observed" ? usage.sent : null,
    included: plan.status === "Observed" && plan.matches ? terms.includedMonthlyEmails : null,
    costOre: usage.status === "Observed" ? usage.estimatedExtraCostOre : null,
  };
}

/**
 * One wording per cause, by what the reader can do about it: wait (no answer, or the provider is limiting the reads),
 * check the key's permission, or read the runbook.
 */
export type AllowanceFailureGroup = "noAnswer" | "rateLimited" | "refused" | "malformed";

const FAILURE_GROUP: Readonly<Record<EmailAllowanceFailure, AllowanceFailureGroup>> = {
  Timeout: "noAnswer",
  Unreachable: "noAnswer",
  ProviderError: "noAnswer",
  RateLimited: "rateLimited",
  Unauthorized: "refused",
  Forbidden: "refused",
  Malformed: "malformed",
};

export function allowanceFailureGroup(reason: EmailAllowanceFailure): AllowanceFailureGroup {
  return FAILURE_GROUP[reason];
}

/**
 * Why "Ingår gratis" and the cost are unknown while the count may still show: the provider names another plan, names
 * none this code knows, or could not be asked. Null when the terms apply, or when no provider is configured (that
 * state has its own single line).
 */
export type AllowancePlanState =
  | { readonly kind: "differs"; readonly active: Exclude<EmailAllowanceDto["terms"]["plan"], "Unknown"> }
  | { readonly kind: "undetermined" }
  | { readonly kind: "failed" };

export function allowancePlanState(allowance: EmailAllowanceDto): AllowancePlanState | null {
  const { usage, plan } = allowance;
  if (usage.status === "NotObserved") return null;
  switch (plan.status) {
    case "Observed":
      if (plan.matches) return null;
      return plan.active === "Unknown" ? { kind: "undetermined" } : { kind: "differs", active: plan.active };
    case "Failed":
      return { kind: "failed" };
    case "NotObserved":
      // Not reachable while one setting selects both reads; if it ever is, the plan was not determined.
      return { kind: "undetermined" };
  }
}

/** The first instant of a `YYYY-MM` month in UTC, the zone the label is formatted in. The browser's clock never decides it. */
export function monthStart(month: string): Date {
  return new Date(`${month}-01T00:00:00Z`);
}
