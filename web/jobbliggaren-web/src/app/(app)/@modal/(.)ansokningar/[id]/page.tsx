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
      // #315 (ADR 0086): the modal header MUST agree with the body
      // (ApplicationDetail, headless). The body shows the "Om annonsen (sparad
      // kopia)" panel with the PRESERVED title when the live ad is archived
      // (jobAd == null) but a snapshot exists — so the modal's title (the
      // dialog's accessible name via aria-labelledby) must show the same
      // preserved title, not the generic mono "#id" fallback. Mirror the exact
      // 3-state precedence the component uses: live → preserved → generic-#id.
      const preservedAd = application.preservedAd ?? null;
      const showPreservedAd = jobAd == null && preservedAd != null;
      const title = hasIdentity
        ? jobAd.title
        : showPreservedAd
          ? preservedAd.title
          : t("ansokningar.detail.fallbackTitle", { shortId });
      // Subtitle precedence mirrors the title: live → "{company} · #shortId";
      // preserved → "{company} · sparad kopia · #shortId" (saved-copy marker,
      // same key shape as the component's preservedAd.headerCompany); generic
      // (no snapshot) → "Skapad {date}" (created-date metadata, NOT an echo of
      // the "#shortId"-fallback title — design-reviewer F5 Major #2 2026-05-20).
      const subtitle = hasIdentity
        ? t("ansokningar.detail.subtitle", {
            company: jobAd.company,
            shortId,
          })
        : showPreservedAd
          ? t("ansokningar.detail.preservedSubtitle", {
              company: preservedAd.company,
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
          // mono only for the truly-no-identity case (no live ad AND no
          // snapshot). The preserved title is real prose, so it renders
          // non-mono — matching the component header (#315).
          mono={!hasIdentity && !showPreservedAd}
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
            <p className="text-body-sm text-text-primary">
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
            <p className="text-body-sm text-text-primary">
              {t("common.errorBodyRetry")}
            </p>
          </div>
        </ApplicationModalShell>
      );
  }
}
