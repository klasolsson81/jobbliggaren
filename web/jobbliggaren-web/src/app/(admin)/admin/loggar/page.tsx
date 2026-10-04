import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminLogsView } from "@/components/admin/admin-logs-view";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.logs");
  return { title: t("meta.security") };
}

/** `/admin/loggar` — the security view, the first of the three log views. */
export default function AdminSecurityLogPage() {
  return <AdminLogsView view="security" />;
}
