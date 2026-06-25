import { notFound, redirect } from "next/navigation";
import { getFormatter, getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getApplicationById } from "@/lib/api/applications";
import { ApplicationDetail } from "@/components/applications/application-detail";
import { ApplicationModalShell } from "@/components/applications/application-modal-shell";
import { WithdrawApplicationButton } from "@/components/applications/withdraw-application-button";
import { getAllowedTransitions } from "@/lib/applications/status";
import { formatDate } from "@/lib/i18n/format";

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
 * Soft-nav (radklick → Link /ansokningar/[id]) fångas här → modal.
 * Hard-nav / refresh / delad länk träffar `/ansokningar/[id]/page.tsx`
 * (fullsida). Samma `getApplicationById` + `ApplicationDetail` i båda
 * (ADR 0053, DRY).
 *
 * RSC: server-fetch här; endast modal-chromet (ApplicationModalShell) +
 * mutationsformulären är "use client". ApplicationDetail-trädet förblir
 * Server Component (passeras som children — serialiserbart RSC-träd, ingen
 * funktion över gränsen). WithdrawApplicationButton är en "use client"-ö
 * som passeras som footer-children (server-renderat träd, ej funktion-prop).
 */
export default async function InterceptedAnsokanModal({ params }: PageProps) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const format = await getFormatter();
  const { id } = await params;
  const result = await getApplicationById(id);

  switch (result.kind) {
    case "ok": {
      const application = result.data;
      const jobAd = application.jobAd ?? null;
      const shortId = application.id.slice(0, 8);
      const hasIdentity = jobAd != null;
      const title = hasIdentity
        ? jobAd.title
        : t("ansokningar.detail.fallbackTitle", { shortId });
      // !hasIdentity: titel = "Ansökan #shortId"-fallback; ekas EJ som
      // subtitle (duplikat). Skapad-datum = informativ metadata istället
      // (design-reviewer F5 Major #2 2026-05-20).
      const subtitle = hasIdentity
        ? t("ansokningar.detail.subtitle", {
            company: jobAd.company,
            shortId,
          })
        : t("ansokningar.detail.createdSubtitle", {
            date: formatDate(format, application.createdAt) ?? "",
          }).trim();
      const canWithdraw = getAllowedTransitions(
        application.status
      ).includes("Withdrawn");

      return (
        <ApplicationModalShell
          title={title}
          subtitle={subtitle}
          mono={!hasIdentity}
          footer={
            canWithdraw ? (
              <WithdrawApplicationButton
                applicationId={application.id}
                currentStatus={application.status}
              />
            ) : null
          }
        >
          <ApplicationDetail application={application} headless />
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
          <div className="jp-modal__body">
            <p className="text-body-sm text-text-secondary">
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
            <p className="text-body-sm text-text-secondary">
              {t("common.errorBodyRetry")}
            </p>
          </div>
        </ApplicationModalShell>
      );
  }
}
