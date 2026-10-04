import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminEmailDelivery } from "@/components/admin/admin-email-delivery";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.email");
  return { title: t("meta.title") };
}

/** `/admin/e-post` — email delivery per email type (ADR 0150). The outcomes are #1981. */
export default function AdminEmailDeliveryPage() {
  return <AdminEmailDelivery region={{ kind: "unavailable" }} />;
}
