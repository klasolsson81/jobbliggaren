import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { AdminOverviewLive } from "@/components/admin/admin-overview-live";
import { loadAdminOverview } from "@/lib/api/admin-overview";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.overview");
  return { title: t("meta.title") };
}

export default async function AdminOverviewPage() {
  const result = await loadAdminOverview();
  if (result.kind === "unauthorized") redirect("/logga-in");
  if (result.kind === "forbidden") redirect("/");
  return <AdminOverviewLive initial={result.data} initialNow={result.loadedAt} />;
}