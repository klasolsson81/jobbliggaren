import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminLogsView } from "@/components/admin/admin-logs-view";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.logs");
  return { title: t("meta.errors") };
}

/** `/admin/loggar/applikationsfel` — application errors, grouped by source and message. */
export default function AdminErrorLogPage() {
  return <AdminLogsView view="errors" />;
}
