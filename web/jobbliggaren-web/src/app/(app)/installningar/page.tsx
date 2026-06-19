import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
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

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-col gap-2">
        <h1 className="jp-h1">Inställningar</h1>
        <p className="jp-lede">
          Hantera dina kontouppgifter och inställningar.
        </p>
      </header>

      {profileResult.kind === "ok" ? (
        <SettingsForm
          initialProfile={profileResult.data}
          userEmail={user.email}
          taxonomy={taxonomy}
        />
      ) : (
        <p className="text-body text-text-secondary">
          {profileResult.kind === "notFound"
            ? "Din profil är inte skapad ännu. Fyll i uppgifterna nedan för att komma igång."
            : profileResult.kind === "rateLimited"
              ? `För många förfrågningar. Försök igen om ${profileResult.retryAfterSeconds} sekunder.`
              : "Profilen kunde inte hämtas just nu. Försök ladda om sidan om en stund."}
        </p>
      )}
    </div>
  );
}
