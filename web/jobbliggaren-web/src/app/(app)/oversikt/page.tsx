import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { getPipeline } from "@/lib/api/applications";
import { getSavedJobAds } from "@/lib/api/saved-job-ads";
import { getRecentSearches } from "@/lib/api/recent-searches";
import { getMatchCount } from "@/lib/api/match-count";
import { getNewFollowedCompanyAdCount } from "@/lib/api/company-follows";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { hasSeenSetupWelcome } from "@/lib/onboarding/setup-welcome";
import { OversiktPage } from "@/components/oversikt/oversikt-page";
import { MatchSetupLauncher } from "@/components/onboarding/match-setup-launcher";
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

export default async function OversiktRoute({
  searchParams,
}: {
  // ?matchsetup=1 — notisen "Ställ in matchning" öppnar rail-modalen (epik #526);
  // mirror /cv:s ?matchning=1-prompt-precedent.
  searchParams: Promise<{ matchsetup?: string }>;
}) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  // #742 — starta taxonomi-hämtningen EAGER (oawaitad) så den överlappar fan-out:en
  // istället för att serialisera en round-trip EFTER den för setup-gren-användare
  // (första-gången/onboarding = appens långsammaste paint). Konsumeras först när
  // `shouldMountSetup` (nedan); för icke-setup-laddningar slängs löftet. Det ligger
  // off the critical path (körs parallellt med fan-out:en, resultatet konsumeras
  // aldrig) → noll adderad wall-clock-latency. `getTaxonomyTree` returnerar ett
  // Result och kastar aldrig i en giltig request-scope (cookie-läsning + try/catch),
  // så det svävande löftet ger ingen unhandled rejection. Data-cachen är per-
  // användare (Authorization-nyckel, `private`, revalidate 3600) — första
  // laddningen/timme är alltså kall men oblockerande.
  const taxonomyPromise = getTaxonomyTree();
  const [
    profile,
    pipeline,
    savedJobAds,
    recentSearches,
    matchCount,
    newFollowedCompanyAdCount,
  ] = await Promise.all([
    getMyProfile(),
    getPipeline(),
    // #726 — deadline-notisen läser nu riktig `expiresAt` ur de sparade
    // annonserna (findUpcomingSavedJobDeadlines).
    getSavedJobAds(),
    // includeCount=false skippar slow per-row JobAds-COUNT (TD-94). /oversikt
    // använder bara label + lastViewedAt för senaste-sökning-notisen; "N nya
    // träffar" hämtas lazy klient-side (SavedSearchNoticeText).
    getRecentSearches(false),
    // ADR 0079 STEG 6 — live match-count för notisen. Degraderar CIVILT och
    // OBEROENDE av övriga källor: ett fel (nätverk/auth mid-render/rate-limit)
    // får aldrig reject:a Promise.all eller redirecta — den löses till null
    // nedan så resten av feeden renderar och match-notisen bara utelämnas
    // (ALDRIG fallback till mock-siffran). `kind: "ok"` → number, allt annat →
    // null. Notera: till skillnad från de auth-kritiska källorna driver ett
    // unauthorized HÄR ingen redirect (notisen är icke-kritisk yta).
    getMatchCount(),
    // Bevakning F2 (#801, RF-6=6B) — "Nya annonser från bevakade företag"-count
    // (nya sedan senaste /foretag-besök) för Företagsbevaknings-notisen (#726).
    // Degraderar CIVILT och OBEROENDE precis som `getMatchCount`: ett fel får
    // aldrig reject:a Promise.all eller redirecta — det löses till 0 nedan.
    getNewFollowedCompanyAdCount(),
  ]);

  // Unauthorized mid-render (token expired mellan layout-check och här):
  // redirecta. Övriga fel ⇒ degraderad render i OversiktPage.
  if (
    profile.kind === "unauthorized" ||
    pipeline.kind === "unauthorized" ||
    savedJobAds.kind === "unauthorized" ||
    recentSearches.kind === "unauthorized"
  ) {
    redirect("/logga-in");
  }

  const displayName =
    profile.kind === "ok" ? profile.data.displayName : null;

  // ADR 0079 STEG 6 — live match-count → number | null. Endast `ok` ger en
  // siffra; alla andra Result-kinds (unauthorized/rateLimited/error) blir null
  // ⇒ match-notisen utelämnas (degraderad render), aldrig mock-fallback.
  const matchCountValue = matchCount.kind === "ok" ? matchCount.data.count : null;

  // Bevakning F2 (#801, RF-6=6B) — live "Nya annonser från bevakade företag"-count → number. Endast
  // `ok` ger den riktiga siffran; alla andra Result-kinds (unauthorized mid-render / rateLimited /
  // error) degraderar till 0 (honest fallback — raden visar alltid en siffra, aldrig en mock). En
  // unauthorized HÄR driver ingen redirect (icke-kritisk yta, paritet `newMatchCount`).
  const newFollowedCompanyAdCountValue =
    newFollowedCompanyAdCount.kind === "ok" ? newFollowedCompanyAdCount.data.count : 0;

  // ADR 0077 STEG 5 — välkomst-/första-setup-modal. Visas bara när profilen
  // laddats, inget yrke ännu angetts (`hasStatedDesiredOccupation`) OCH cookien
  // saknas (användaren har inte redan stängt/skippat den i denna webbläsare).
  // Cookien bryter "om-nagg vid tom preferens"-loopen utan backend-skrivning
  // (ADR 0076 Decision 3). Setup-nudgen i Översikt-feeden lever kvar som
  // komplementär post-skip-påminnelse.
  const setupWelcomeSeen = await hasSeenSetupWelcome();
  const { matchsetup: matchsetupParam } = await searchParams;

  // Matchnings-setup behövs så länge inget yrke angetts. Modalen auto-öppnas i
  // två fall: (a) nytt konto som inte redan stängt/skippat den i denna webbläsare
  // (cookien), ELLER (b) användaren klickade notisen "Ställ in matchning"
  // (?matchsetup=1). Notisen visas bara för needsSetup-användare, så param-vägen
  // ligger alltid inom needsSetup. Cookien bryter "om-nagg vid tom preferens"-
  // loopen utan backend-skrivning (ADR 0076 Decision 3); notisen är den
  // komplementära post-skip-ingången.
  const needsSetup =
    profile.kind === "ok" && !profile.data.hasStatedDesiredOccupation;
  const showWelcome = needsSetup && !setupWelcomeSeen;
  const openSetupFromParam = matchsetupParam === "1";
  const shouldMountSetup = needsSetup && (showWelcome || openSetupFromParam);

  // Taxonomin behövs ENBART när modalen faktiskt ska monteras (yrkes-/region-/
  // anställningsform-väljaren behöver trädet). Löftet startades EAGER ovan (#742)
  // så det redan är i luften parallellt med fan-out:en; här awaitas det bara i
  // setup-grenen — icke-setup-laddningar konsumerar det aldrig. Degraderar civilt:
  // utan taxonomi visas ingen modal (väljaren vore tom) → modalen utelämnas hellre
  // än renderas trasig.
  const taxonomy = shouldMountSetup
    ? await taxonomyPromise.then((r) => (r.kind === "ok" ? r.data : null))
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
        matchCount={matchCountValue}
        newFollowedCompanyAdCount={newFollowedCompanyAdCountValue}
      />
      {shouldMountSetup && taxonomy !== null && profile.kind === "ok" && (
        <MatchSetupLauncher
          autoOpen
          occupationFields={taxonomy.occupationFields}
          regions={taxonomy.regions}
          employmentTypes={taxonomy.employmentTypes}
          persistedOccupationGroups={profile.data.preferredOccupationGroups}
          persistedRegions={profile.data.preferredRegions}
          persistedMunicipalities={profile.data.preferredMunicipalities}
          persistedEmploymentTypes={profile.data.preferredEmploymentTypes}
          persistedSkills={profile.data.preferredSkills}
          persistedOccupationExperience={profile.data.preferredOccupationExperience}
          importCvHref={IMPORT_CV_HREF}
        />
      )}
      {/* DEV-ONLY — not rendered in production; remove before launch (Klas).
          Lets Klas wipe his own test data and re-run onboarding from scratch. */}
      {process.env.NODE_ENV !== "production" && <ResetMyDataNote />}
    </>
  );
}
