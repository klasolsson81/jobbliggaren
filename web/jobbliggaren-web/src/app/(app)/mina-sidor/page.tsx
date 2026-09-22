import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { resolveSkillLabels } from "@/lib/api/skills";
import { SettingsForm } from "@/components/settings/settings-form";
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
 * The notFound branch (a new user without a profile row) renders an empty state rather than a blank
 * screen after login (Wroblewski 2008).
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
    <div className="flex flex-col gap-6">
      <header className="flex flex-col gap-2">
        <h1 className="jp-h1">{t("minaSidor.title")}</h1>
        <p className="jp-lede">{t("minaSidor.lede")}</p>
      </header>

      {profileResult.kind === "ok" ? (
        <SettingsForm
          initialProfile={profileResult.data}
          userEmail={user.email}
          taxonomy={taxonomy}
          initialSkillGroups={initialSkillGroups}
        />
      ) : (
        <p className="text-body text-text-primary">
          {profileResult.kind === "notFound"
            ? t("minaSidor.profileNotCreated")
            : profileResult.kind === "rateLimited"
              ? t("minaSidor.rateLimited", {
                  seconds: profileResult.retryAfterSeconds,
                })
              : t("minaSidor.profileLoadError")}
        </p>
      )}
    </div>
  );
}
