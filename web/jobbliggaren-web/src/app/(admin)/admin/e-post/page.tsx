import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminEmailAllowance, type AdminEmailAllowanceRegion } from "@/components/admin/admin-email-allowance";
import { AdminEmailDelivery } from "@/components/admin/admin-email-delivery";
import { AdminPageHeader } from "@/components/admin/admin-page-header";
import { loadEmailAllowance } from "@/lib/api/admin-email";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.email");
  return { title: t("meta.title") };
}

/**
 * `/admin/e-post` (ADR 0150; #1981 PR 1, ADR 0178): first this month's mail against the provider's free allowance and
 * the cost beyond it, then delivery per email type, which is #1981's later PRs and says "Kommer snart" until then.
 * The allowance read is its own region: whatever it answers, the rest of the page renders (ADR 0150 D2).
 */
export default async function AdminEmailPage() {
  const t = await getTranslations("admin");
  const read = await loadEmailAllowance();
  const allowance: AdminEmailAllowanceRegion = read.kind === "ok" ? { kind: "loaded", data: read.data } : { kind: "failed" };
  // A refusal says why, since trying again later does not help it; any other failure takes the region's shared line.
  const failed = read.kind === "unauthorized" || read.kind === "forbidden" ? t(`users.errors.${read.kind}`) : undefined;

  return (
    <div className="flex flex-col gap-8">
      <AdminPageHeader title={t("email.heading")} lede={t("email.lede")} />
      <AdminEmailAllowance region={allowance} failed={failed} />
      <AdminEmailDelivery region={{ kind: "unavailable" }} />
    </div>
  );
}
