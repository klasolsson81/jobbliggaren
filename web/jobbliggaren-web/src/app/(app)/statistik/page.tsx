import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ArrowLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getApplicationStats } from "@/lib/api/applications";
import { assertNever } from "@/lib/dto/_helpers";
import { ApplicationStats } from "@/components/applications/application-stats";

/**
 * #313 — `/statistik` (BUILD.md §6.2: avslags-analys, pipeline-konvertering).
 * Top-level route (NOT nested under /ansokningar) to dodge the
 * `@modal/(.)ansokningar/[id]` intercept that would otherwise catch a soft-nav to
 * `/ansokningar/statistik` and open it as an application-detail modal — the same
 * resolution #316/#332 used for `/aktivitetsrapport` (senior-cto-advisor bind
 * 2026-06-29). Reached via an in-page link from /ansokningar, not the app-shell
 * PRIMARY_NAV. Per-user data: no shared cache.
 */
export const dynamic = "force-dynamic";

export default async function StatistikPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("statistik");

  const result = await getApplicationStats();
  switch (result.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "rateLimited":
      return <ErrorShell title={t("error.title")} body={t("error.rateLimited")} />;
    case "notFound":
    case "forbidden":
    case "error":
      return <ErrorShell title={t("error.title")} body={t("error.body")} />;
    default:
      return assertNever(result);
  }

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("title")}</h1>
            <p className="jp-pagehero__lede">{t("lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <Link
          href="/ansokningar"
          className="mb-4 inline-flex items-center gap-1.5 text-text-primary hover:underline"
        >
          <ArrowLeft size={16} aria-hidden="true" />
          {t("back")}
        </Link>

        <ApplicationStats data={result.data} />
      </div>
    </>
  );
}

function ErrorShell({ title, body }: { title: string; body: string }) {
  return (
    <div className="jp-container jp-page">
      <div className="jp-page__title-block">
        <h1 className="jp-page__title">{title}</h1>
        <p className="jp-page__lede">{body}</p>
      </div>
    </div>
  );
}
