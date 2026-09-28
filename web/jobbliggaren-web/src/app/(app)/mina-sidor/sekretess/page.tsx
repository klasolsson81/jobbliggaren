import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { PrivacyCard } from "@/components/settings/privacy-card";
import { MinaSidorShell } from "@/components/settings/mina-sidor-shell";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("minaSidor.sections.sekretess") };
}

/**
 * `/mina-sidor/sekretess` (#1891): the export (not built yet) and deleting the account. It reads only
 * the session's address, so no profile result can take it away (design-reviewer Major 3, #1740).
 */
export default async function MinaSidorSekretessPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  return (
    <MinaSidorShell active="sekretess">
      <PrivacyCard userEmail={user.email} />
    </MinaSidorShell>
  );
}
