import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { NotificationsSection } from "@/components/settings/notifications-section";
import { MinaSidorShell, ProfileUnavailable } from "@/components/settings/mina-sidor-shell";
import type { Metadata } from "next";
import { PageFeedback } from "@/components/feedback/page-feedback";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("minaSidor.sections.notiser") };
}

/**
 * `/mina-sidor/notiser` (#1891): both notification consents and the cadence they share. The
 * notification mails' Art. 7(3) withdrawal link lands here (`EmailTemplates.cs`), and
 * `NotificationMailLinksLandOnServedRoutesTests` reads this file's path to prove it.
 */
export default async function MinaSidorNotiserPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const profileResult = await getMyProfile();
  if (profileResult.kind === "unauthorized") redirect("/logga-in");

  return (
    <>
      <MinaSidorShell active="notiser">
        {profileResult.kind === "ok" ? (
          <NotificationsSection
            initialMatchEnabled={profileResult.data.backgroundMatchNotificationsEnabled}
            initialFollowEnabled={profileResult.data.followedCompanyNotificationsEnabled}
            initialCadence={profileResult.data.digestCadence}
          />
        ) : (
          <ProfileUnavailable
            title={t("minaSidor.sections.notiser")}
            retryAfterSeconds={
              profileResult.kind === "rateLimited" ? profileResult.retryAfterSeconds : undefined
            }
          />
        )}
      </MinaSidorShell>
      <PageFeedback pageKey="my-pages" />
    </>
  );
}
