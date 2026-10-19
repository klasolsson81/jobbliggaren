import type { ReactNode } from "react";
import { useFormatter, useTranslations } from "next-intl";
import {
  allowanceFailureGroup,
  allowanceFigures,
  allowancePlanState,
  monthStart,
} from "@/lib/admin/email-allowance";
import type { AdminEmailAllowance as Allowance, AdminValueRegion } from "@/lib/admin/view-models";
import { formatDateTime, formatLedgerDate } from "@/lib/i18n/format";
import { AdminUnknown } from "./admin-unknown";

const HEADING_ID = "admin-email-allowance";

/** The region is built, so it is never "Kommer snart": it is loading, failed or loaded (ADR 0150 D2). */
export type AdminEmailAllowanceRegion = Exclude<AdminValueRegion<Allowance>, { readonly kind: "unavailable" }>;

interface Line {
  readonly key: string;
  readonly text: string;
  /** A read that failed, which the region announces once in the form "{source}: {state}". */
  readonly failure?: boolean;
}

/**
 * This month's mail against the provider's free allowance (#1981 PR 1, ADR 0178; ADR 0150 Amendment 2026-10-19).
 *
 * The ledger form of every admin page but the overview (ADR 0150 D1): a section with an h2 and the house summary row,
 * never a card, a meter or a percentage. The three values are the provider's count, the configured plan's free mails
 * and the computed cost; an unknown one is an en-dash, never 0, and a computed 0 is a value. The lines under the row
 * say why a value is unknown; the note says where the figures come from. A cost above 0 is a fact, not a fault, so
 * nothing here takes a status colour.
 */
export function AdminEmailAllowance({
  region,
  failed,
}: {
  readonly region: AdminEmailAllowanceRegion;
  /** The failed line in place of the shared one, for a refusal whose remedy is not to try again. */
  readonly failed?: string;
}) {
  const t = useTranslations("admin.email.allowance");
  const admin = useTranslations("admin");
  const format = useFormatter();
  const allowance = region.kind === "loaded" ? region.data : null;
  const usage = allowance?.usage ?? null;
  const figures = allowance === null ? null : allowanceFigures(allowance);
  const month = usage?.status === "Observed" ? usage.month : null;
  const costCurrency = allowance?.terms.costCurrency ?? null;

  const money = (amount: number, code: string) => format.number(amount, { style: "currency", currency: code });
  const value = (shown: string | null): ReactNode => (shown === null ? <AdminUnknown /> : shown);
  const sent = figures === null || figures.sent === null ? null : format.number(figures.sent);
  const included = figures === null || figures.included === null ? null : format.number(figures.included);
  const cost = figures === null || figures.costOre === null || costCurrency === null
    ? null
    : money(figures.costOre / 100, costCurrency);

  const lines = stateLines();
  const failures = lines.filter((line) => line.failure).map((line) => line.text);

  function stateLines(): ReadonlyArray<Line> {
    if (region.kind === "failed") return [{ key: "failed", text: failed ?? admin("regions.failed"), failure: true }];
    if (region.kind === "loading") return [{ key: "loading", text: admin("regions.loading") }];
    if (allowance === null || usage === null) return [];
    if (usage.status === "NotObserved") return [{ key: "notConfigured", text: t("notConfigured") }];

    const out: Line[] = [];
    if (usage.status === "Failed") {
      out.push({ key: "usage", text: t(`failure.${allowanceFailureGroup(usage.reason)}`), failure: true });
    }
    const plan = allowancePlanState(allowance);
    if (plan?.kind === "differs") {
      out.push({ key: "plan", text: t("plan.differs", { active: plan.active, configured: allowance.terms.plan }) });
    } else if (plan !== null) {
      out.push({ key: "plan", text: t(`plan.${plan.kind}`), failure: plan.kind === "failed" });
    }
    if (usage.status === "Observed") {
      // ADR 0161 D4 trigger 1: more than half of the send limit is a state of the month, not a term.
      if (usage.aboveHalfOfSendLimit) {
        out.push({ key: "limit", text: t("sendLimitAboveHalf", { limit: format.number(allowance.terms.monthlySendLimit) }) });
      }
      // A webhook nobody registered is a deviation; an unreadable count is a check that did not run (ADR 0178 D8).
      if (usage.webhooksCount === null) out.push({ key: "webhooks", text: t("webhooksUnreadable") });
      else if (usage.webhooksCount > 0) out.push({ key: "webhooks", text: t("webhooks", { count: usage.webhooksCount }) });
    }
    return out;
  }

  return (
    <section aria-labelledby={HEADING_ID} className="flex flex-col gap-4">
      <h2 id={HEADING_ID} className="jp-h2">
        {month === null
          ? t("headingThisMonth")
          : t("heading", { month: format.dateTime(monthStart(month), { month: "long", year: "numeric", timeZone: "UTC" }) })}
      </h2>
      {failures.length > 0 || region.kind === "loading" ? (
        <p className="sr-only" role={failures.length > 0 ? "alert" : "status"}>
          {admin("overview.observation.announcement", {
            source: admin("overview.observation.sources.email"),
            state: failures.length > 0 ? failures.join(" ") : admin("regions.loading"),
          })}
        </p>
      ) : null}

      <dl className="jp-adminmailsum">
        <div>
          <dt>{t("sent")}</dt>
          <dd>{value(sent)}</dd>
        </div>
        <div>
          <dt>{t("included")}</dt>
          <dd>{value(included)}</dd>
        </div>
        <div>
          <dt>{t("cost")}</dt>
          <dd>{value(cost)}</dd>
        </div>
      </dl>

      {lines.map((line) => (
        <p key={line.key} className="jp-adminsoon">
          {line.text}
        </p>
      ))}

      {allowance !== null && usage !== null && usage.status !== "NotObserved" ? (
        <div className="jp-adminmailsum__note">
          {usage.status === "Observed" ? (
            <p>
              {t.rich("source", {
                at: formatDateTime(format, usage.observedAt) ?? usage.observedAt,
                time: (chunks) => <time dateTime={usage.observedAt}>{chunks}</time>,
              })}
            </p>
          ) : null}
          <p>
            {t("price", {
              price: money(allowance.terms.pricePerThousandExtra, allowance.terms.currency),
              per: format.number(1000),
              readOn: formatLedgerDate(format, allowance.terms.priceReadOn) ?? allowance.terms.priceReadOn,
            })}
          </p>
          <p>
            {t("conversion", {
              rate: format.number(allowance.terms.sekPerEur, { maximumFractionDigits: 4 }),
              rateOn: formatLedgerDate(format, allowance.terms.exchangeRateOn) ?? allowance.terms.exchangeRateOn,
            })}
          </p>
          {usage.status === "Observed" && !usage.aboveHalfOfSendLimit ? (
            <p>{t("sendLimit", { limit: format.number(allowance.terms.monthlySendLimit) })}</p>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
