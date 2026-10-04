import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AuditLogTable } from "@/app/(admin)/admin/granskning/audit-log-table";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import { PREVIEW_AUDIT_ENTRIES } from "@/lib/admin-preview/fixtures";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("audit"), robots: { index: false, follow: false } };
}

/** Granskning's table over fixtures, so the preview reads no backend (ADR 0150 D5). */
export default async function AdminPreviewAuditPage() {
  requireAdminPreview();
  const t = await getTranslations("admin");

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="jp-h1">{t("audit.heading")}</h1>
        <p className="jp-lede">{t("audit.lede")}</p>
      </div>
      <AuditLogTable entries={PREVIEW_AUDIT_ENTRIES} />
    </div>
  );
}
