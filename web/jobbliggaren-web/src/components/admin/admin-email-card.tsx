import type { ReactNode } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { allowanceFailureGroup, allowanceFigures, allowancePlanState, monthStart } from "@/lib/admin/email-allowance";
import type { OverviewObservation } from "@/lib/admin/overview";
import type { AdminEmailAllowance, AdminRegionKind, AdminValueRegion } from "@/lib/admin/view-models";
import { AdminObservationNote } from "./admin-observation-note";
import { AdminRegionLine } from "./admin-region-line";
import { AdminUnknown } from "./admin-unknown";

/** A rich-text tag that keeps its formatted value, or shows the en-dash for an unknown one (ADR 0150 D2). */
function knownOr(shown: string | null) {
  return function value(chunks: ReactNode): ReactNode {
    return shown === null ? <AdminUnknown /> : chunks;
  };
}

/**
 * The overview's e-post card body (#1981 PR 1; ADR 0150 Amendment 2026-10-19): this month's sends against the free
 * allowance, and the computed cost under it. Failed sends are the attention card's line (#1981 PR 2), never this
 * card's, and the card is never an attention item: passing the allowance is a cost fact, not a fault.
 *
 * The count is the number. While the plan in force is not the configured one, its unit names only the month and the
 * page's own line says why there is no allowance or cost. With no count the number is an en-dash with its unit hidden,
 * and one line says why.
 */
function EmailRows({ allowance, kind }: {
  readonly allowance: AdminEmailAllowance | null;
  readonly kind: Exclude<AdminRegionKind, "empty">;
}) {
  const t = useTranslations("admin.overview.email");
  const lines = useTranslations("admin.email.allowance");
  const format = useFormatter();
  const figures = allowance === null ? null : allowanceFigures(allowance);
  const usage = allowance?.usage ?? null;

  if (allowance === null || usage === null || figures === null || figures.sent === null) {
    const why = kind !== "loaded" ? <AdminRegionLine quiet kind={kind} className="jp-adminkpi__sub" />
      : usage?.status === "Failed" ? <p className="jp-adminkpi__sub">{lines(`failure.${allowanceFailureGroup(usage.reason)}`)}</p>
      : usage?.status === "ReadKeyMissing" ? <p className="jp-adminkpi__sub">{lines("readKeyMissing")}</p>
      : <p className="jp-adminkpi__sub">{lines("notConfigured")}</p>;
    return (
      <>
        <p className="jp-adminkpi">
          <span className="jp-adminkpi__value"><AdminUnknown /></span>
        </p>
        {why}
      </>
    );
  }

  const month = usage.status === "Observed" && usage.month !== null
    ? format.dateTime(monthStart(usage.month), { month: "long", year: "numeric", timeZone: "UTC" })
    : null;
  const plan = allowancePlanState(allowance);
  if (plan !== null) {
    return (
      <>
        <p className="jp-adminkpi">
          <span className="jp-adminkpi__value">{format.number(figures.sent)}</span>
          <span className="jp-adminkpi__unit">{month === null ? t("unitSentOnlyThisMonth") : t("unitSentOnly", { month })}</span>
        </p>
        <p className="jp-adminkpi__sub">
          {plan.kind === "differs"
            ? lines("plan.differs", { active: plan.active, configured: allowance.terms.plan })
            : lines(`plan.${plan.kind}`)}
        </p>
      </>
    );
  }

  const included = figures.included === null ? null : format.number(figures.included);
  const cost = figures.costOre === null ? null
    : format.number(figures.costOre / 100, { style: "currency", currency: allowance.terms.costCurrency });
  const unit = month === null
    ? t.rich("unitThisMonth", { included: included ?? "", value: knownOr(included) })
    : t.rich("unit", { included: included ?? "", month, value: knownOr(included) });

  return (
    <>
      <p className="jp-adminkpi">
        <span className="jp-adminkpi__value">{format.number(figures.sent)}</span>
        <span className="jp-adminkpi__unit">{unit}</span>
      </p>
      <p className="jp-adminkpi__sub">{t.rich("cost", { cost: cost ?? "", value: knownOr(cost) })}</p>
    </>
  );
}

/** The card from a fixed region: the preview and the tests, which have no observation time. */
export function AdminEmailBody({ region }: { readonly region: AdminValueRegion<AdminEmailAllowance> }) {
  return <EmailRows allowance={region.kind === "loaded" ? region.data : null} kind={region.kind} />;
}

/** The card from the overview's observation, dated by when the provider answered (`observedAt`). */
export function AdminObservedEmail({ observation, now }: {
  readonly observation: OverviewObservation<AdminEmailAllowance>;
  readonly now: number;
}) {
  const loaded = observation.kind === "loaded" || observation.kind === "empty";
  return (
    <>
      <EmailRows allowance={loaded ? observation.data : null} kind={loaded ? "loaded" : observation.kind} />
      {loaded && observation.data.usage.status === "NotObserved"
        ? null
        : <AdminObservationNote observation={observation} now={now} />}
    </>
  );
}
