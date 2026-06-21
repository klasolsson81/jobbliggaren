import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { CreateResumeForm } from "@/components/resumes/create-resume-form";
import { RouteModalShell } from "@/components/modals/route-modal-shell";

/**
 * Intercepting Route för @modal-slotten. `(.)cv/ny` matchar samma segment-
 * nivå som slot-monteringspunkten `(app)` — `@modal` är en slot, INTE ett
 * route-segment (Next-docs Intercepting Routes §Convention + §Modals,
 * verifierat node_modules/next/dist/docs Next 16.2.x).
 *
 * Soft-nav (Link /cv/ny från /cv) fångas här → modal. Hard-nav / refresh /
 * delad länk träffar `(app)/cv/ny/page.tsx` (fullsida). Samma
 * `CreateResumeForm` i båda (ADR 0053, DRY).
 *
 * RSC: auth-grind på servern; endast modal-chromet (RouteModalShell) och
 * CreateResumeForm är "use client". Skapa-flödet är oförändrat: server-
 * actionen redirectar till /cv/{id} vid 201 — en full navigation som ersätter
 * modalen med det nya CV:t. Stäng (ESC/scrim/X/Avbryt) → router.back() → /cv.
 */
export default async function InterceptedCvNyModal() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");

  return (
    <RouteModalShell
      title={t("cv.new.title")}
      description={t("cv.new.modalDescription")}
    >
      <div className="jp-modal__body">
        <CreateResumeForm />
      </div>
    </RouteModalShell>
  );
}
