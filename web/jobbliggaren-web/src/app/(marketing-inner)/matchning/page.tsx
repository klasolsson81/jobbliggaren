import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { MatchChip } from "@/components/job-ads/match-chip";
import type { MatchGrade } from "@/lib/dto/job-ad-match";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("content-matchning");
  return {
    title: t("meta.title"),
    description: t("meta.description"),
  };
}

type CompareRow = { part: string; what: string; cv: boolean };
type GradeItem = { grade: MatchGrade; req: string };
type OrderStep = { rule: string; outcome: string };

/**
 * Publik förklaringssida: Så fungerar matchningen (#365). Statisk RSC.
 * Förklarar den deterministiska, AI-fria matchningsmotorn i klarspråk med
 * tema-säkra visualiseringar byggda på BEFINTLIGA klasser/komponenter (den
 * riktiga `MatchChip` = grad-stegen; `jp-tag` = exempel-chips; `jp-card`/
 * border-tokens = jämför-rutorna). Inga nya globals.css-klasser, inga
 * gradienter/glow utöver hero, ingen hårdkodad hex. Civic-utility: en h1,
 * h2-outline, hög-kontrast text, ingen em-dash, inget procenttal (Goodhart).
 */
export default async function MatchningPage() {
  const t = await getTranslations("content-matchning");
  const youItems = t.raw("deterministic.youItems") as string[];
  const adItems = t.raw("deterministic.adItems") as string[];
  const detParagraphs = t.raw("deterministic.paragraphs") as string[];
  const compareRows = t.raw("compare.rows") as CompareRow[];
  const gradeItems = t.raw("grades.items") as GradeItem[];
  const orderSteps = t.raw("order.steps") as OrderStep[];
  const notScored = t.raw("notScored.items") as string[];
  const transParagraphs = t.raw("transparency.paragraphs") as string[];
  const youHave = t.raw("transparency.youHave") as string[];
  const missing = t.raw("transparency.missing") as string[];

  return (
    <main id="main" tabIndex={-1} className="focus:outline-none">
      <section className="jp-pagehero" aria-labelledby="matchning-heading">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 id="matchning-heading" className="jp-pagehero__title">
              {t("title")}
            </h1>
          </div>
        </div>
      </section>

      <div className="mx-auto w-full max-w-2xl px-6 py-12">
        <p className="text-body-sm text-text-secondary">{t("updated")}</p>
        <p className="mt-4 text-body text-text-primary">{t("intro")}</p>

        {/* 1. Deterministic + compare diagram */}
        <h2 className="mt-12 text-body-lg font-semibold text-text-primary">
          {t("deterministic.heading")}
        </h2>
        {detParagraphs.map((paragraph, i) => (
          <p key={`det-${i}`} className="mt-3 text-body text-text-primary">
            {paragraph}
          </p>
        ))}
        <div className="mt-5 flex flex-col items-stretch gap-3 sm:flex-row sm:items-center">
          <div className="flex-1 rounded-md border border-border-strong bg-surface-secondary p-4">
            <p className="text-label font-semibold text-text-primary">
              {t("deterministic.youProvide")}
            </p>
            <ul className="mt-2 flex flex-col gap-1 text-body-sm text-text-primary">
              {youItems.map((item, i) => (
                <li key={`you-${i}`}>{item}</li>
              ))}
            </ul>
          </div>
          <p
            className="text-center font-mono text-caption text-text-secondary"
            aria-hidden="true"
          >
            {t("deterministic.compareLabel")}
          </p>
          <div className="flex-1 rounded-md border border-border-strong bg-surface-secondary p-4">
            <p className="text-label font-semibold text-text-primary">
              {t("deterministic.adProvides")}
            </p>
            <ul className="mt-2 flex flex-col gap-1 text-body-sm text-text-primary">
              {adItems.map((item, i) => (
                <li key={`ad-${i}`}>{item}</li>
              ))}
            </ul>
          </div>
        </div>

        {/* 2. What we compare */}
        <h2 className="mt-12 text-body-lg font-semibold text-text-primary">
          {t("compare.heading")}
        </h2>
        <p className="mt-3 text-body text-text-primary">{t("compare.intro")}</p>
        <ul className="mt-4 flex flex-col gap-3">
          {compareRows.map((row) => (
            <li
              key={row.part}
              className="rounded-md border border-border-default p-3"
            >
              <p className="flex flex-wrap items-center gap-2 text-body font-semibold text-text-primary">
                {row.part}
                {row.cv ? (
                  <span className="jp-tag" data-tag="status-neutral">
                    {t("compare.needsCv")}
                  </span>
                ) : null}
              </p>
              <p className="mt-1 text-body-sm text-text-primary">{row.what}</p>
            </li>
          ))}
        </ul>

        {/* 3. The grades (the ladder = the real chips, Topp -> Grund) */}
        <h2 className="mt-12 text-body-lg font-semibold text-text-primary">
          {t("grades.heading")}
        </h2>
        <p className="mt-3 text-body text-text-primary">{t("grades.intro")}</p>
        <ul className="mt-4 flex flex-col gap-3">
          {gradeItems.map((item) => (
            <li
              key={item.grade}
              className="flex flex-col gap-1.5 rounded-md border border-border-default p-3 sm:flex-row sm:items-baseline sm:gap-3"
            >
              <span className="shrink-0">
                <MatchChip grade={item.grade} />
              </span>
              <span className="text-body-sm text-text-primary">
                <span className="font-semibold">{t("grades.reqLabel")}</span>{" "}
                {item.req}
              </span>
            </li>
          ))}
        </ul>
        <div className="mt-4 rounded-md border border-border-default p-3">
          <p className="flex flex-col gap-1.5 text-body-sm text-text-primary sm:flex-row sm:items-baseline sm:gap-3">
            <span className="shrink-0">
              <MatchChip grade="Related" />
            </span>
            <span>
              <span className="font-semibold">{t("grades.relatedLabel")}</span>{" "}
              {t("grades.relatedReq")}
            </span>
          </p>
        </div>
        <p className="mt-4 text-body-sm text-text-secondary">
          {t("grades.topNote")}
        </p>
        <p className="mt-2 text-body-sm text-text-secondary">
          {t("grades.cvNote")}
        </p>

        {/* 4. The order of the gates */}
        <h2 className="mt-12 text-body-lg font-semibold text-text-primary">
          {t("order.heading")}
        </h2>
        <p className="mt-3 text-body text-text-primary">{t("order.intro")}</p>
        <ol className="mt-4 flex flex-col gap-3">
          {orderSteps.map((step, i) => (
            <li
              key={`step-${i}`}
              className="rounded-md border border-border-default p-3"
            >
              <p className="text-body text-text-primary">
                <span className="font-mono text-caption text-text-secondary">
                  {i + 1}
                </span>{" "}
                {step.rule}
              </p>
              <p className="mt-1 text-body-sm font-semibold text-text-primary">
                {step.outcome}
              </p>
            </li>
          ))}
        </ol>
        <p className="mt-4 text-body-sm text-text-secondary">
          {t("order.worstNote")}
        </p>

        {/* 5. What we deliberately do not score */}
        <h2 className="mt-12 text-body-lg font-semibold text-text-primary">
          {t("notScored.heading")}
        </h2>
        <p className="mt-3 text-body text-text-primary">{t("notScored.intro")}</p>
        <ul className="mt-3 flex list-disc flex-col gap-2 pl-5 text-body text-text-primary">
          {notScored.map((item, i) => (
            <li key={`ns-${i}`}>{item}</li>
          ))}
        </ul>
        <p className="mt-3 text-body text-text-primary">{t("notScored.why")}</p>

        {/* 6. Transparency */}
        <h2 className="mt-12 text-body-lg font-semibold text-text-primary">
          {t("transparency.heading")}
        </h2>
        {transParagraphs.map((paragraph, i) => (
          <p key={`tr-${i}`} className="mt-3 text-body text-text-primary">
            {paragraph}
          </p>
        ))}
        <div className="mt-4 rounded-md border border-border-default p-4">
          <p className="text-label font-semibold text-text-primary">
            {t("transparency.youHaveLabel")}
          </p>
          <div className="mt-2 flex flex-wrap gap-2">
            {youHave.map((item) => (
              <span key={item} className="jp-tag" data-tag="status-success">
                {item}
              </span>
            ))}
          </div>
          <p className="mt-4 text-label font-semibold text-text-primary">
            {t("transparency.missingLabel")}
          </p>
          <div className="mt-2 flex flex-wrap gap-2">
            {missing.map((item) => (
              <span key={item} className="jp-tag" data-tag="status-neutral">
                {item}
              </span>
            ))}
          </div>
        </div>

        {/* 7. CTA */}
        <div className="mt-12 rounded-md border border-border-default bg-surface-secondary p-5">
          <h2 className="text-body-lg font-semibold text-text-primary">
            {t("cta.heading")}
          </h2>
          <p className="mt-2 text-body text-text-primary">{t("cta.text")}</p>
          <p className="mt-4">
            <Link href="/registrera" className="jp-btn jp-btn--primary">
              {t("cta.button")}
            </Link>
          </p>
        </div>
      </div>
    </main>
  );
}
