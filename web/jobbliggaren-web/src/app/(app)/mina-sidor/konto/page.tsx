import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { AccountSection } from "@/components/settings/account-section";
import { MinaSidorShell } from "@/components/settings/mina-sidor-shell";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("minaSidor.sections.konto") };
}

/** `/mina-sidor/konto` (#1891): changing the address, and the language. */
export default async function MinaSidorKontoPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const profileResult = await getMyProfile();
  if (profileResult.kind === "unauthorized") redirect("/logga-in");

  return (
    <MinaSidorShell active="konto">
      <AccountSection
        email={user.email}
        language={profileResult.kind === "ok" ? profileResult.data.language : null}
        retryAfterSeconds={
          profileResult.kind === "rateLimited" ? profileResult.retryAfterSeconds : undefined
        }
      />
    </MinaSidorShell>
  );
}
