import { notFound, redirect } from "next/navigation";
import { getFormatter, getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getApplicationById } from "@/lib/api/applications";
import { applicationDetailHeader } from "@/lib/applications/header";
import { ApplicationDetailBody } from "@/components/applications/application-detail-body";
import { ApplicationModalShell } from "@/components/applications/application-modal-shell";

interface PageProps {
  params: Promise<{ id: string }>;
}

/**
 * Intercepting Route för @modal-slotten. `(.)ansokningar/[id]` matchar
 * samma segment-nivå som slot-monteringspunkten `(app)` — `@modal` är en
 * slot, INTE ett route-segment, så `ansokningar` ligger en segment-nivå upp
 * trots två fil-nivåer (Next-docs Intercepting Routes §Convention + §Modals,
 * verifierat node_modules/next/dist/docs Next 16.2.x — "the `(..)`
 * convention is based on route segments, not the file-system … does not
 * consider `@slot` folders"). Identiskt mönster med F3
 * `@modal/(.)jobb/[id]` (ADR 0053).
 *
 * Klas-beslut 2026-07-10 (ansokningar-audit, reverterar ADR 0092 D7:s
 * drawer-halva — se ADR 0092 Livscykel-amendment): soft-nav (radklick →
 * Link /ansokningar/[id]) fångas här → CENTRERAD MODAL (ApplicationModalShell,
 * samma paradigm som `/jobb` och gäst-flödet — ADR 0053 gäller åter oinskränkt).
 * Hard-nav / refresh / delad länk träffar `/ansokningar/[id]/page.tsx`
 * (fullsidan), som renderar samma kropp under samma header
 * (`applicationDetailHeader`).
 *
 * RSC: server-fetch här; endast modal-chromet (ApplicationModalShell) och
 * kroppens mutationsöar är "use client". ApplicationDetailBody-trädet förblir
 * Server Component (passeras som children — serialiserbart RSC-träd, ingen
 * funktion över gränsen).
 */
export default async function InterceptedAnsokanModal({ params }: PageProps) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const tf = await getTranslations("fallback");
  const format = await getFormatter();
  const { id } = await params;
  const result = await getApplicationById(id);

  switch (result.kind) {
    case "ok": {
      const application = result.data;
      const { title, subtitle } = applicationDetailHeader(application, t, format);
      // `now` is the per-request reference time for the day count in the
      // status block (server-computed; the read is fetched fresh on each open).
      return (
        <ApplicationModalShell title={title} subtitle={subtitle}>
          {/* jp-modal__body äger padding + intern scroll i .jp-modal-flexkolumnen
              (samma anropar-wrapp som @modal/(.)jobb) — utan den svämmar kroppen
              över panelens max-height. */}
          <div className="jp-modal__body">
            <ApplicationDetailBody
              application={application}
              now={new Date()}
              titleLevel={2}
            />
          </div>
        </ApplicationModalShell>
      );
    }
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "rateLimited":
      return (
        <ApplicationModalShell
          title={t("common.rateLimitedTitle")}
          subtitle=""
        >
          {/* id="jp-modal-desc" så modal-skalets aria-describedby aldrig dinglar
              även i fel-grenarna (bodyn bär det i ok-fallet). */}
          <div className="jp-modal__body">
            <p id="jp-modal-desc" className="text-body-sm text-text-primary">
              {t("common.rateLimitedBody", {
                seconds: result.retryAfterSeconds,
              })}
            </p>
          </div>
        </ApplicationModalShell>
      );
    case "forbidden":
    case "error":
      return (
        <ApplicationModalShell
          title={t("ansokningar.detail.loadErrorTitle")}
          subtitle=""
        >
          <div className="jp-modal__body">
            <p id="jp-modal-desc" className="text-body-sm text-text-primary">
              {tf("errorBodyRetry")}
            </p>
          </div>
        </ApplicationModalShell>
      );
  }
}
