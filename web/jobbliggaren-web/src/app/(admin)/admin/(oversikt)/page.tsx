import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminOverview } from "@/components/admin/admin-overview";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.overview");
  return { title: t("meta.title") };
}

/**
 * `/admin` — the admin overview (ADR 0150 D1). Its route group keeps a later `loading.tsx` from
 * wrapping the other admin pages.
 */
export default function AdminOverviewPage() {
  return <AdminOverview />;
}
