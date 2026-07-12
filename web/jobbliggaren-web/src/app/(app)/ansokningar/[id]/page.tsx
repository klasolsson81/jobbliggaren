import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getApplicationById } from "@/lib/api/applications";
import { ApplicationDetail } from "@/components/applications/application-detail";
import { WithdrawApplicationButton } from "@/components/applications/withdraw-application-button";
import { DeleteApplicationButton } from "@/components/applications/delete-application-button";
import { getAllowedTransitions } from "@/lib/applications/status";

interface Props {
  // Next.js 16 App Router: params är Promise (verifierat mot
  // node_modules/next/dist/docs file-conventions).
  params: Promise<{ id: string }>;
}

/**
 * Fullsida för en ansökan (`/ansokningar/[id]`). Renderas vid hard-nav /
 * sidladdning / delad länk. Vid soft-nav från listan fångar
 * `@modal/(.)ansokningar/[id]` istället och visar samma `ApplicationDetail`
 * i modal (ADR 0053 — en presentationskomponent, två kontexter). Speglar
 * F3 `/jobb/[id]/page.tsx` exakt: samma `.jp-modal`-panel utan
 * skugga/animation/max-höjd, .jp-container/.jp-page-wrap.
 *
 * notFound (okänt id) → Next `notFound()`. unauthorized → `/logga-in`.
 * rateLimited/error → civil felruta.
 */
export default async function AnsokanDetailPage({ params }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const { id } = await params;
  const result = await getApplicationById(id);

  switch (result.kind) {
    case "ok": {
      const application = result.data;
      const canWithdraw = getAllowedTransitions(
        application.status
      ).includes("Withdrawn");

      return (
        <div className="jp-container jp-page">
          <Link
            href="/ansokningar"
            className="jp-btn jp-btn--ghost jp-btn--sm"
          >
            <ChevronLeft size={14} aria-hidden="true" />{" "}
            {t("ansokningar.detail.backLink")}
          </Link>
          <div
            className="jp-modal"
            style={{
              width: "100%",
              maxWidth: 760,
              maxHeight: "none",
              marginInline: "auto",
              marginTop: 16,
              boxShadow: "none",
              animation: "none",
            }}
          >
            <ApplicationDetail application={application} />
            <div className="jp-modal__foot">
              <span className="jp-modal__foot__spacer" />
              {/* #782 (ADR 0104) — always available (deletion has no transition
                  precondition, unlike Withdraw). "Ta bort ansökan" = HARD remove;
                  "Återta ansökan" = keep as a terminal status. */}
              <DeleteApplicationButton applicationId={application.id} />
              {canWithdraw && (
                <WithdrawApplicationButton
                  applicationId={application.id}
                  currentStatus={application.status}
                />
              )}
              <Link
                href="/ansokningar"
                className="jp-btn jp-btn--secondary"
              >
                {t("common.back")}
              </Link>
            </div>
          </div>
        </div>
      );
    }
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "rateLimited":
      return (
        <div className="jp-container jp-page">
          <div
            role="alert"
            className="rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4"
          >
            <p className="text-body font-medium text-warning-700">
              {t("common.rateLimitedTitle")}
            </p>
            <p className="mt-1 text-body-sm text-warning-700">
              {t("common.rateLimitedBody", {
                seconds: result.retryAfterSeconds,
              })}
            </p>
          </div>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="jp-container jp-page">
          <div className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
            <p className="text-body font-medium">
              {t("ansokningar.detail.loadErrorTitle")}
            </p>
            <p className="mt-1 text-body-sm">{t("common.errorBodyReload")}</p>
          </div>
        </div>
      );
  }
}
