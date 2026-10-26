import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminEmailAllowance, type AdminEmailAllowanceRegion } from "@/components/admin/admin-email-allowance";
import { AdminEmailDelivery } from "@/components/admin/admin-email-delivery";
import { AdminEmailDmarc } from "@/components/admin/admin-email-dmarc";
import { AdminPageHeader } from "@/components/admin/admin-page-header";
import { emailDeliveryPeriod } from "@/lib/admin/email-delivery";
import type { AdminEmailRead } from "@/lib/admin/view-models";
import {
  loadEmailAllowance,
  loadEmailDelivery,
  loadEmailDmarc,
  loadEmailFailures,
  loadEmailKinds,
  type EmailRead,
} from "@/lib/api/admin-email";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.email");
  return { title: t("meta.title") };
}

/**
 * `/admin/e-post` (ADR 0150; #1981, ADR 0178): this month's mail against the provider's free allowance and the cost
 * beyond it, then Leverans for the period in the URL (`?period=h24|d3|d7`, 7 dygn when it is missing or unknown):
 * the provider's counts and failed sends, and our own count per mail kind (#1981 PR 3); then the DMARC reports over their
 * own fixed window, which Leverans's period does not drive (#1981 PR 4b).
 * Each read is its own region: whatever one answers, the rest of the page renders (ADR 0150 D2). A period change is a
 * navigation that keeps this page on screen until the new reads answer, so the allowance never shows a loading state
 * for it and the activated link keeps focus.
 */
export default async function AdminEmailPage({
  searchParams,
}: {
  searchParams: Promise<Readonly<Record<string, string | string[] | undefined>>>;
}) {
  const period = emailDeliveryPeriod((await searchParams).period);
  const t = await getTranslations("admin");
  const [allowanceRead, deliveryRead, failuresRead, kindsRead, dmarcRead] = await Promise.all([
    loadEmailAllowance(),
    loadEmailDelivery(period),
    loadEmailFailures(period),
    loadEmailKinds(period),
    loadEmailDmarc(),
  ]);

  // A refusal says why, since trying again later does not help it; any other failure takes the region's shared line.
  const refusal = (read: EmailRead<unknown>) =>
    read.kind === "unauthorized" || read.kind === "forbidden" ? t(`users.errors.${read.kind}`) : undefined;
  function region<T>(read: EmailRead<T>): AdminEmailRead<T> {
    return read.kind === "ok" ? { kind: "loaded", data: read.data } : { kind: "failed", line: refusal(read) };
  }
  const allowance: AdminEmailAllowanceRegion =
    allowanceRead.kind === "ok" ? { kind: "loaded", data: allowanceRead.data } : { kind: "failed" };

  return (
    <div className="flex flex-col gap-8">
      <AdminPageHeader title={t("email.heading")} lede={t("email.lede")} />
      <AdminEmailAllowance region={allowance} failed={refusal(allowanceRead)} />
      <AdminEmailDelivery
        pagePath="/admin/e-post"
        period={period}
        summary={region(deliveryRead)}
        failures={region(failuresRead)}
        kinds={region(kindsRead)}
      />
      <AdminEmailDmarc read={region(dmarcRead)} />
    </div>
  );
}
