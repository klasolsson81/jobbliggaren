import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { resolveSkillLabels } from "@/lib/api/skills";
import { MatchPreferencesCard } from "@/components/settings/match-preferences-card";
import { MinaSidorShell, ProfileUnavailable } from "@/components/settings/mina-sidor-shell";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("minaSidor.sections.matchning") };
}

/**
 * `/mina-sidor` — the account's own page (#1740), since #1891 one section at a time behind a menu.
 * This is its Matchning section and the page every link to bare `/mina-sidor` opens: the header's
 * user menu, `MATCH_SETTINGS_HREF`, the retired settings paths (308s
 * in `next.config.ts`), and the settings link in notification mails sent before #1891.
 * It sits in the `(matchning)` route group so its loading state cannot become the fallback of the
 * other sections, as `cv/(hub)` does for `/cv` (#1385).
 */
export default async function MinaSidorMatchningPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("settings");

  // The taxonomy feeds the card's pickers. A failed read is `null`, and the card degrades to one
  // sentence rather than failing the page.
  const [profileResult, taxonomyResult] = await Promise.all([
    getMyProfile(),
    getTaxonomyTree(),
  ]);
  if (profileResult.kind === "unauthorized") redirect("/logga-in");

  const taxonomy = taxonomyResult.kind === "ok" ? taxonomyResult.data : null;

  // Reverse-resolve the saved skill concept-ids to GROUPS server-side (ADR 0047 + #277): the flat
  // skill taxonomy is never shipped to the web as a tree, so without this seed the card would render
  // raw concept-ids on a cold load, and a saved twin-pair renders as ONE chip. Failure (or a missing
  // profile) leaves an empty list; the card keeps its id fallback.
  const skillGroupsResult =
    profileResult.kind === "ok"
      ? await resolveSkillLabels(profileResult.data.preferredSkills)
      : null;
  const initialSkillGroups = skillGroupsResult?.kind === "ok" ? skillGroupsResult.data : [];

  return (
    <MinaSidorShell active="matchning">
      {profileResult.kind === "ok" ? (
        <MatchPreferencesCard
          occupationFields={taxonomy?.occupationFields ?? []}
          regions={taxonomy?.regions ?? []}
          employmentTypes={taxonomy?.employmentTypes ?? []}
          initialOccupationGroups={profileResult.data.preferredOccupationGroups}
          initialRegions={profileResult.data.preferredRegions}
          initialMunicipalities={profileResult.data.preferredMunicipalities}
          initialRemote={profileResult.data.preferredRemote}
          initialEmploymentTypes={profileResult.data.preferredEmploymentTypes}
          initialSkills={profileResult.data.preferredSkills}
          initialSkillGroups={initialSkillGroups}
          initialExperienceYears={profileResult.data.experienceYears}
          initialOccupationExperience={profileResult.data.preferredOccupationExperience}
          degraded={taxonomy === null}
        />
      ) : (
        <ProfileUnavailable
          title={t("matchPrefs.title")}
          retryAfterSeconds={
            profileResult.kind === "rateLimited" ? profileResult.retryAfterSeconds : undefined
          }
        />
      )}
    </MinaSidorShell>
  );
}
