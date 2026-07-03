import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { resolveSkillLabels } from "@/lib/api/skills";
import { SettingsForm } from "@/components/settings/settings-form";

/**
 * `/installningar` — v3-version av användarens inställningssida (F6 Prompt 2,
 * ADR 0057). Ersätter `/mig`. Klas-direktiv: tema/lang/aviseringar/sekretess
 * + logga ut samlade på en route (CTO 2026-05-20 Val 1A).
 *
 * Server-component-shell: hämtar session + profil, lyfter till
 * `<SettingsForm />` client-island som håller direct-apply-state. Profil-
 * fetch är samma `getMyProfile()` som tidigare `/mig` — ingen ny endpoint.
 *
 * notFound-grenen (ny användare utan profil-rad) hanteras genom att rendera
 * tom-state med samma copy som tidigare `/mig`-routens fallback — bevarar
 * UX-kontraktet (Wroblewski 2008: aldrig blank skärm efter login).
 */
export default async function InstallningarPage() {
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
        <h1 className="jp-h1">{t("installningar.title")}</h1>
        <p className="jp-lede">{t("installningar.lede")}</p>
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
            ? t("installningar.profileNotCreated")
            : profileResult.kind === "rateLimited"
              ? t("installningar.rateLimited", {
                  seconds: profileResult.retryAfterSeconds,
                })
              : t("installningar.profileLoadError")}
        </p>
      )}
    </div>
  );
}
