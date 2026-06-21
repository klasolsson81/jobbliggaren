import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { getPipeline } from "@/lib/api/applications";
import { getSavedJobAds } from "@/lib/api/saved-job-ads";
import { getRecentSearches } from "@/lib/api/recent-searches";
import { getResumes } from "@/lib/api/resumes";
import { fetchLandingStats } from "@/lib/api/landing";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { hasSeenSetupWelcome } from "@/lib/onboarding/setup-welcome";
import { OversiktPage } from "@/components/oversikt/oversikt-page";
import { WelcomeSetupModal } from "@/components/onboarding/welcome-setup-modal";
import { ResetMyDataNote } from "@/components/dev/reset-my-data-note";

/** CV-importflödets route (wizardens yrkes-steg tom-state-länk). */
const IMPORT_CV_HREF = "/cv/importera";

/**
 * F6 P5 Punkt 4 — `/oversikt` route. Per-user-data: ingen delad cache.
 * CTO-dom 2026-05-24 D2: `force-dynamic` + per-request `Promise.all` mot
 * 5-6 befintliga endpoints. Inget composer-endpoint, ingen Worker-cache.
 *
 * GDPR + ADR 0045 klass (a) auth-gated 300ms p95: ingen shared cache.
 */
export const dynamic = "force-dynamic";

export default async function OversiktRoute() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  // PERF svans-PR2 (2026-05-24): bytt getJobAds() → getLandingStats() för
  // "Aktiva annonser totalt"-fältet. Samma värde (activeCount), men landing-
  // stats är Worker-precomputed Redis-cache 0-1ms vs ListJobAdsQuery p50
  // ~1.2s / max ~7s (CloudWatch-discovery 2026-05-24). Eliminerar 1-2s från
  // Promise.all-max + löser samtidigt 28-vs-9 mismatch i HeaderStats
  // (HeaderStats använder samma endpoint). CTO-godkänd discovery-baserad
  // fix (agentId ad37955db80099f19). Landing-stats är publik anonym ADR 0064
  // — säker att läsa även från auth-gated route.
  const [profile, pipeline, savedJobAds, recentSearches, resumes, landingStats] =
    await Promise.all([
      getMyProfile(),
      getPipeline(),
      getSavedJobAds(),
      // svans-PR4 perf-fix: includeCount=false skippar slow per-row JobAds-
      // COUNT (TD-94 rot) som triggade FE-timeout 8s → Npgsql 57014 (Klas
      // 2026-05-24). /oversikt använder bara label + lastViewedAt — currentCount
      // är 0 vilket är OK eftersom Sammanfattningen inte renderar "(N nya)".
      getRecentSearches(false),
      getResumes(1, 20),
      fetchLandingStats(),
    ]);

  // Unauthorized mid-render (token expired mellan layout-check och här):
  // redirecta. Övriga fel ⇒ degraderad render i OversiktPage.
  // (landingStats är anonym så har ingen unauthorized-väg.)
  if (
    profile.kind === "unauthorized" ||
    pipeline.kind === "unauthorized" ||
    savedJobAds.kind === "unauthorized" ||
    recentSearches.kind === "unauthorized" ||
    resumes.kind === "unauthorized"
  ) {
    redirect("/logga-in");
  }

  const displayName =
    profile.kind === "ok" ? profile.data.displayName : null;

  // ADR 0077 STEG 5 — välkomst-/första-setup-modal. Visas bara när profilen
  // laddats, inget yrke ännu angetts (`hasStatedDesiredOccupation`) OCH cookien
  // saknas (användaren har inte redan stängt/skippat den i denna webbläsare).
  // Cookien bryter "om-nagg vid tom preferens"-loopen utan backend-skrivning
  // (ADR 0076 Decision 3). Setup-nudgen i Översikt-feeden lever kvar som
  // komplementär post-skip-påminnelse.
  const setupWelcomeSeen = await hasSeenSetupWelcome();
  const showWelcome =
    profile.kind === "ok" &&
    !profile.data.hasStatedDesiredOccupation &&
    !setupWelcomeSeen;

  // Taxonomi hämtas ENBART när välkomsten faktiskt ska visas (wizardens
  // yrkes-/region-/anställningsform-väljare behöver trädet). Ingen extra fetch
  // för de allra flesta sid-laddningar. Degraderar civilt: utan taxonomi visas
  // ingen wizard (väljaren vore tom) → modalen utelämnas hellre än renderas
  // trasig.
  const taxonomy = showWelcome
    ? await getTaxonomyTree().then((r) => (r.kind === "ok" ? r.data : null))
    : null;

  return (
    <>
      <OversiktPage
        email={user.email}
        displayName={displayName}
        profile={profile}
        pipeline={pipeline}
        savedJobAds={savedJobAds}
        recentSearches={recentSearches}
        resumes={resumes}
        landingStats={landingStats}
      />
      {showWelcome && taxonomy !== null && profile.kind === "ok" && (
        <WelcomeSetupModal
          showWelcome
          occupationFields={taxonomy.occupationFields}
          regions={taxonomy.regions}
          employmentTypes={taxonomy.employmentTypes}
          persistedOccupationGroups={profile.data.preferredOccupationGroups}
          persistedRegions={profile.data.preferredRegions}
          persistedMunicipalities={profile.data.preferredMunicipalities}
          persistedEmploymentTypes={profile.data.preferredEmploymentTypes}
          importCvHref={IMPORT_CV_HREF}
        />
      )}
      {/* DEV-ONLY — not rendered in production; remove before launch (Klas).
          Lets Klas wipe his own test data and re-run onboarding from scratch. */}
      {process.env.NODE_ENV !== "production" && <ResetMyDataNote />}
    </>
  );
}
