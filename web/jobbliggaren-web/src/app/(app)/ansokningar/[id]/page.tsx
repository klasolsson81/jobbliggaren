import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getFormatter, getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getApplicationById } from "@/lib/api/applications";
import { applicationDetailHeader } from "@/lib/applications/header";
import { ApplicationDetailBody } from "@/components/applications/application-detail-body";
import { ApplicationLoadError } from "@/components/applications/application-load-error";
import { DeleteApplicationButton } from "@/components/applications/delete-application-button";
import type { Metadata } from "next";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

/**
 * The title resolves against the record's ABSENCE: a missing record must not serve this
 * route's title over a "Sidan finns inte" body, and `(app)/not-found.tsx` cannot correct
 * that (`lib/metadata/not-found-title.ts` records why). The gate is `kind === "notFound"`
 * and nothing else — both halves are pinned by
 * `(app)/detail-route-not-found-title.test.ts`.
 */
export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { id } = await params;
  const result = await getApplicationById(id);
  if (result.kind === "notFound") return notFoundMetadata();

  const t = await getTranslations("pages");
  return { title: t("ansokningar.detail.meta.title") };
}

interface Props {
  // Next.js 16 App Router: params är Promise (verifierat mot
  // node_modules/next/dist/docs file-conventions).
  params: Promise<{ id: string }>;
}

/**
 * Fullsida för en ansökan (`/ansokningar/[id]`). Renderas vid hard-nav /
 * sidladdning / delad länk. Vid soft-nav från listan fångar
 * `@modal/(.)ansokningar/[id]` istället. Båda renderar samma
 * `ApplicationDetailBody` under samma `applicationDetailHeader` (ADR 0053 —
 * en presentationskomponent, två kontexter). Fullsidan lägger till sin
 * tillbakalänk, sin h1 och sin fot. Speglar F3 `/jobb/[id]/page.tsx`: samma
 * `.jp-modal`-panel utan skugga/animation/max-höjd, .jp-container/.jp-page.
 *
 * notFound (okänt id) → Next `notFound()`. unauthorized → `/logga-in`.
 * rateLimited/error → det delade felblocket, under tillbakalänken.
 */
export default async function AnsokanDetailPage({ params }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const format = await getFormatter();
  const { id } = await params;
  const result = await getApplicationById(id);

  switch (result.kind) {
    case "ok": {
      const application = result.data;
      const { title, subtitle } = applicationDetailHeader(application, t, format);

      return (
        <div className="jp-container jp-page">
          <BackLink label={t("ansokningar.detail.backLink")} />
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
            <header className="jp-modal__head">
              <div style={{ flex: 1 }}>
                <h1 className="jp-modal__title">{title}</h1>
                <p className="jp-modal__company">{subtitle}</p>
              </div>
            </header>
            <div className="jp-modal__body">
              <ApplicationDetailBody
                application={application}
                now={new Date()}
                titleLevel={1}
              />
            </div>
            <div className="jp-modal__foot">
              <span className="jp-modal__foot__spacer" />
              <DeleteApplicationButton applicationId={application.id} />
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
          <BackLink label={t("ansokningar.detail.backLink")} />
          <div className="mt-4">
            <ApplicationLoadError
              title={t("common.rateLimitedTitle")}
              body={t("common.rateLimitedBody", {
                seconds: result.retryAfterSeconds,
              })}
            />
          </div>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="jp-container jp-page">
          <BackLink label={t("ansokningar.detail.backLink")} />
          <div className="mt-4">
            <ApplicationLoadError
              title={t("ansokningar.detail.loadErrorTitle")}
              body={t("common.errorBodyReload")}
            />
          </div>
        </div>
      );
  }
}

function BackLink({ label }: { label: string }) {
  return (
    <Link href="/ansokningar" className="jp-btn jp-btn--ghost jp-btn--sm">
      <ChevronLeft size={14} aria-hidden="true" /> {label}
    </Link>
  );
}
