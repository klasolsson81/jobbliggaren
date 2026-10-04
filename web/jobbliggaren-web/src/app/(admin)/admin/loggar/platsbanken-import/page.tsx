import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminLogsView } from "@/components/admin/admin-logs-view";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.logs");
  return { title: t("meta.imports") };
}

/** `/admin/loggar/platsbanken-import` — the Platsbanken import runs. */
export default function AdminImportLogPage() {
  return <AdminLogsView view="imports" />;
}
