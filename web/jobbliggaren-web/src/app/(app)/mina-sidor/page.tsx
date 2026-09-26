import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { resolveSkillLabels } from "@/lib/api/skills";
import { SettingsForm } from "@/components/settings/settings-form";
import { AccountCards } from "@/components/settings/account-cards";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("minaSidor.meta.title") };
}

/**
 * `/mina-sidor` — the account's own page (#1740, epic #1732 part 3b). It replaced the settings page
 * of ADR 0057, whose two earlier paths survive only as 308s (`next.config.ts`). Language, the
 * notification consents, matching, the address, deleting the account and logging out share one
 * route (CTO 2026-05-20 Val 1A).
 *
 * Server-component shell: fetches the session + profile and lifts them into the `<SettingsForm />`
 * client island, which holds the direct-apply state.
 *
 * Every card but the account cards reads the profile, so a profile that cannot be read takes their
 * place with one sentence. The account cards read only the session's address and render on every
 * branch (design-reviewer Major 3, #1740). `getMyProfile` answers the backend's 404 as `error`, so a
 * missing profile takes the same branch.
 */
export default async function MinaSidorPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");

  // Profil + taxonomi parallellt (Promise.all) — taxonomin matar matchnings-
  // kortets väljare. Taxonomi-fel ⇒ `null` (kortet degraderar civilt, kraschar
  // inte); profilen styr fortfarande resten av sidan.
  const [profileResult, taxonomyResult] = await Promise.all([
    getMyProfile(),
    getTaxonomyTree(),
  ]);
  if (profileResult.kind === "unauthorized") redirect("/logga-in");

  const taxonomy =
    taxonomyResult.kind === "ok" ? taxonomyResult.data : null;

  // Reverse-resolve the saved skill concept-ids to GROUPS server-side (ADR
  // 0047 + #277): the flat skill taxonomy is never shipped to the FE as a tree,
  // so without this seed the matchnings-kort would render raw concept-ids on a
  // cold load. The result is grouped by shared exact-label surface, so a saved
  // twin-pair renders as ONE chip. Depends on the profile, so it runs after the
  // parallel fetch. Failure (or a missing profile) → empty list; the card keeps
  // its graceful id-fallback. Unknown/removed ids are dropped by the backend.
  const skillGroupsResult =
    profileResult.kind === "ok"
      ? await resolveSkillLabels(profileResult.data.preferredSkills)
      : null;
  const initialSkillGroups =
    skillGroupsResult?.kind === "ok" ? skillGroupsResult.data : [];

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("minaSidor.title")}</h1>
            <p className="jp-pagehero__lede">{t("minaSidor.lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {profileResult.kind === "ok" ? (
          <SettingsForm
            initialProfile={profileResult.data}
            userEmail={user.email}
            taxonomy={taxonomy}
            initialSkillGroups={initialSkillGroups}
          />
        ) : (
          <div className="jp-settings-grid">
            <div className="jp-settings-grid__col">
              <p className="text-body text-text-primary">
                {profileResult.kind === "rateLimited"
                  ? t("minaSidor.rateLimited", {
                      seconds: profileResult.retryAfterSeconds,
                    })
                  : t("minaSidor.profileLoadError")}
              </p>
            </div>
            <div className="jp-settings-grid__col">
              <AccountCards email={user.email} />
            </div>
          </div>
        )}
      </div>
    </>
  );
}
