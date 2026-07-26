import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { CvUploadForm } from "@/components/resumes/cv-upload-form";
import { RouteModalShell } from "@/components/modals/route-modal-shell";

/**
 * Intercepting Route för @modal-slotten. `(.)cv/importera` matchar samma
 * segment-nivå som slot-monteringspunkten `(app)` — `@modal` är en slot,
 * INTE ett route-segment, så `cv` ligger en segment-nivå upp trots fler
 * fil-nivåer (Next-docs Intercepting Routes §Convention + §Modals,
 * verifierat node_modules/next/dist/docs Next 16.2.x).
 *
 * Soft-nav (Link /cv/importera från /cv) fångas här → modal. Hard-nav /
 * refresh / delad länk träffar `(app)/cv/importera/page.tsx` (fullsida).
 * Samma `CvUploadForm` i båda (ADR 0053, DRY).
 *
 * RSC: auth-grind på servern; endast modal-chromet (RouteModalShell) och
 * CvUploadForm är "use client". Upload-flödet (CV-pivot 5c): formuläret rutar på
 * utfallet — Promoted → router.push('/cv/[id]/granska'), LeftPending →
 * router.push('/cv/granska/[parsedId]') — en full navigation som ersätter
 * modalen. Personnummer-fyndet reser samtyckesdialogen i formuläret innan
 * ruttningen. Stäng (ESC/scrim/X) → router.back() → /cv.
 */
export default async function InterceptedCvImportModal() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");

  // Samma namn-hantering som fullsidan (ADR 0053, DRY): fältet är CV:ts ETIKETT och står
  // tomt: lämnas det tomt genererar servern ett icke-PII-namn. Varken profilen eller
  // filnamnet används (#1060; ADR 0096 D-B).
  return (
    <RouteModalShell
      title={t("cv.import.title")}
      description={t("cv.import.modalDescription")}
    >
      <div className="jp-modal__body">
        <CvUploadForm />
      </div>
    </RouteModalShell>
  );
}
