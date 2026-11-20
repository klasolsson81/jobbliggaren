import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { getRecentSearches } from "@/lib/api/recent-searches";
import { QuickLinksSection } from "@/components/settings/quick-links-section";
import { MINA_SIDOR_HEADING_ID, MinaSidorShell, ProfileUnavailable } from "@/components/settings/mina-sidor-shell";
import type { Metadata } from "next";
import { PageFeedback } from "@/components/feedback/page-feedback";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("minaSidor.sections.snabblankar") };
}

/**
 * `/mina-sidor/snabblankar` (#2265, ADR 0188): the quick links in the /jobb hero. The junior link and the user's
 * recent searches, each added or removed here; the hero's × and /sokningar's rows call the same actions.
 */
export default async function MinaSidorSnabblankarPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const [profileResult, searchesResult] = await Promise.all([getMyProfile(), getRecentSearches()]);
  if (profileResult.kind === "unauthorized" || searchesResult.kind === "unauthorized") redirect("/logga-in");

  const rateLimited =
    profileResult.kind === "rateLimited"
      ? profileResult
      : searchesResult.kind === "rateLimited"
        ? searchesResult
        : undefined;

  return (
    <>
      <MinaSidorShell active="snabblankar">
        {profileResult.kind === "ok" && searchesResult.kind === "ok" ? (
          <QuickLinksSection
            juniorEnabled={profileResult.data.juniorQuickLinkEnabled}
            searches={searchesResult.data}
            headingId={MINA_SIDOR_HEADING_ID}
          />
        ) : (
          <ProfileUnavailable
            title={t("minaSidor.sections.snabblankar")}
            retryAfterSeconds={rateLimited?.retryAfterSeconds}
          />
        )}
      </MinaSidorShell>
      <PageFeedback pageKey="my-pages" />
    </>
  );
}
