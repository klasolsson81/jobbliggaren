import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { StatusPill, type PillTone } from "@/components/ui/status-pill";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("content-cv-granskning");
  return {
    title: t("meta.title"),
    description: t("meta.description"),
  };
}

type VerdictItem = {
  label: string;
  tone: PillTone;
  name: string;
  quote: string | null;
  observation: string | null;
  note: string | null;
  reason: string | null;
};
type Band = { label: string; tone: PillTone };

/**
 * Publik förklaringssida: Så granskar vi ditt CV (#368). Tvilling till
 * /matchning. Statisk RSC. Förklarar den deterministiska, AI-fria CV-motorn och
 * HUR FÖRBÄTTRINGSFÖRSLAG GES (ärligt: förslagen är read-only vägledning man
 * själv arbetar in, motorn skriver aldrig om i tysthet, hittar aldrig på
 * meriter). Visualiseringarna återanvänder den RIKTIGA `StatusPill` + granska-
 * /förbättra-vyns klasser (`jp-criterion__*`, `jp-improve__*`) så de matchar
 * produkten. Inga nya globals.css-klasser, inga foton, inget procenttal/betyg
 * (Goodhart). Alla exempel är tydligt illustrativa, utan personnummer.
 */
export default async function CvGranskningPage() {
  const t = await getTranslations("content-cv-granskning");
  const detParagraphs = t.raw("deterministic.paragraphs") as string[];
  const pipeline = t.raw("deterministic.pipeline") as string[];
  const verdicts = t.raw("verdicts.items") as VerdictItem[];
  const noScoreParagraphs = t.raw("noScore.paragraphs") as string[];
  const bands = t.raw("noScore.bands") as Band[];
  const assessed = t.raw("assessable.assessed") as string[];
  const notAssessed = t.raw("assessable.notAssessed") as string[];
  const pnrParagraphs = t.raw("personnummer.paragraphs") as string[];
  const improveParagraphs = t.raw("improvements.paragraphs") as string[];
  const promiseItems = t.raw("promise.items") as string[];

  return (
    <main id="main" tabIndex={-1} className="focus:outline-none">
      <section className="jp-pagehero" aria-labelledby="cv-granskning-heading">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <p className="jp-pagehero__kicker">{t("kicker")}</p>
            <h1 id="cv-granskning-heading" className="jp-pagehero__title">
              {t("title")}
            </h1>
          </div>
        </div>
      </section>

      <div className="mx-auto w-full max-w-2xl px-6 py-12">
        <p className="text-body-sm text-text-secondary">{t("updated")}</p>
        <p className="mt-4 text-body text-text-primary">{t("intro")}</p>

        {/* 1. Deterministic + pipeline */}
        <h2 className="mt-12 text-body-lg font-semibold text-text-primary">
          {t("deterministic.heading")}
        </h2>
        {detParagraphs.map((paragraph, i) => (
          <p key={`det-${i}`} className="mt-3 text-body text-text-primary">
            {paragraph}
          </p>
        ))}
        <p className="mt-5 text-label font-semibold text-text-primary">
          {t("deterministic.pipelineLabel")}
        </p>
        <ol className="mt-2 flex flex-wrap items-center gap-x-2 gap-y-2">
          {pipeline.map((step, i) => (
            <li key={`pipe-${i}`} className="flex items-center gap-2">
              <span className="jp-tag" data-tag="status-neutral">
                {step}
              </span>
              {i < pipeline.length - 1 ? (
                <span className="text-text-secondary" aria-hidden="true">
                  ›
                </span>
              ) : null}
            </li>
          ))}
        </ol>

        {/* 2. Four verdicts, each with evidence (reuses StatusPill + jp-criterion) */}
        <h2 className="mt-12 text-body-lg font-semibold text-text-primary">
          {t("verdicts.heading")}
        </h2>
        <p className="mt-3 text-body text-text-primary">{t("verdicts.intro")}</p>
        <ul className="mt-4 flex flex-col gap-3">
          {verdicts.map((item) => (
            <li key={item.name} className="jp-criterion">
              <p className="mb-1 text-caption text-text-secondary">
                {t("verdicts.exampleLabel")}
              </p>
              <div className="jp-criterion__head">
                <StatusPill tone={item.tone}>{item.label}</StatusPill>
                <span className="jp-criterion__name">{item.name}</span>
              </div>
              {item.reason !== null ? (
                <p className="jp-criterion__note">{item.reason}</p>
              ) : null}
              {item.quote !== null ? (
                <ul className="jp-criterion__evidence">
                  <li className="jp-criterion__evidence-item">
                    <blockquote className="jp-criterion__quote">
                      {item.quote}
                    </blockquote>
                    {item.note !== null ? (
                      <p className="jp-criterion__note">{item.note}</p>
                    ) : null}
                  </li>
                </ul>
              ) : null}
              {item.observation !== null ? (
                <ul className="jp-criterion__evidence">
                  <li className="jp-criterion__evidence-item">
                    <p className="jp-criterion__note">{item.observation}</p>
                  </li>
                </ul>
              ) : null}
            </li>
          ))}
        </ul>

        {/* 3. No opaque score — bands + honest framing */}
        <h2 className="mt-12 text-body-lg font-semibold text-text-primary">
          {t("noScore.heading")}
        </h2>
        {noScoreParagraphs.map((paragraph, i) => (
          <p key={`ns-${i}`} className="mt-3 text-body text-text-primary">
            {paragraph}
          </p>
        ))}
        <p className="mt-5 text-label font-semibold text-text-primary">
          {t("noScore.bandsLabel")}
        </p>
        <div className="mt-2 flex flex-wrap gap-2">
          {bands.map((band) => (
            <StatusPill key={band.label} tone={band.tone}>
              {band.label}
            </StatusPill>
          ))}
        </div>

        {/* 4. What can / cannot be assessed from text */}
        <h2 className="mt-12 text-body-lg font-semibold text-text-primary">
          {t("assessable.heading")}
        </h2>
        <p className="mt-3 text-body text-text-primary">{t("assessable.intro")}</p>
        <div className="mt-4 grid gap-4 sm:grid-cols-2">
          <div className="rounded-md border border-border-default p-4">
            <p className="text-label font-semibold text-text-primary">
              {t("assessable.assessedLabel")}
            </p>
            <ul className="mt-2 flex list-disc flex-col gap-1.5 pl-5 text-body-sm text-text-primary">
              {assessed.map((item, i) => (
                <li key={`a-${i}`}>{item}</li>
              ))}
            </ul>
          </div>
          <div className="rounded-md border border-border-default p-4">
            <p className="text-label font-semibold text-text-primary">
              {t("assessable.notAssessedLabel")}
            </p>
            <ul className="mt-2 flex list-disc flex-col gap-1.5 pl-5 text-body-sm text-text-primary">
              {notAssessed.map((item, i) => (
                <li key={`na-${i}`}>{item}</li>
              ))}
            </ul>
          </div>
        </div>

        {/* 5. Personnummer + privacy */}
        <h2 className="mt-12 text-body-lg font-semibold text-text-primary">
          {t("personnummer.heading")}
        </h2>
        {pnrParagraphs.map((paragraph, i) => (
          <p key={`pnr-${i}`} className="mt-3 text-body text-text-primary">
            {paragraph}
          </p>
        ))}
        <div className="mt-4">
          <p className="mb-1 text-caption text-text-secondary">
            {t("personnummer.exampleLabel")}
          </p>
          <StatusPill tone="danger">{t("personnummer.exampleFlag")}</StatusPill>
        </div>

        {/* 6. How improvements are given (Klas's focus) — reuses jp-improve markup */}
        <h2 className="mt-12 text-body-lg font-semibold text-text-primary">
          {t("improvements.heading")}
        </h2>
        {improveParagraphs.map((paragraph, i) => (
          <p key={`imp-${i}`} className="mt-3 text-body text-text-primary">
            {paragraph}
          </p>
        ))}
        <p className="mt-5 mb-1 text-caption text-text-secondary">
          {t("improvements.exampleLabel")}
        </p>
        <div className="jp-improve__change">
          <div className="jp-improve__change-head">
            <StatusPill tone="neutral">{t("improvements.pillLabel")}</StatusPill>
          </div>
          <div className="jp-improve__diff">
            <div className="jp-improve__diff-side">
              <span className="jp-improve__diff-label">
                {t("improvements.currentLabel")}
              </span>
              <blockquote className="jp-criterion__quote jp-improve__diff-before">
                {t("improvements.before")}
              </blockquote>
            </div>
            <div className="jp-improve__diff-side">
              <span className="jp-improve__diff-label">
                {t("improvements.suggestionLabel")}
              </span>
              <blockquote className="jp-criterion__quote jp-improve__diff-after">
                {t("improvements.after")}
              </blockquote>
            </div>
          </div>
          <p className="jp-criterion__note">{t("improvements.rationale")}</p>
          <p className="jp-improve__provenance">{t("improvements.source")}</p>
        </div>

        {/* 7. The promise */}
        <h2 className="mt-12 text-body-lg font-semibold text-text-primary">
          {t("promise.heading")}
        </h2>
        <ul className="mt-3 flex list-disc flex-col gap-2 pl-5 text-body text-text-primary">
          {promiseItems.map((item, i) => (
            <li key={`pr-${i}`}>{item}</li>
          ))}
        </ul>

        {/* 8. Links */}
        <div className="mt-12 rounded-md border border-border-default bg-surface-secondary p-5">
          <h2 className="text-body-lg font-semibold text-text-primary">
            {t("links.heading")}
          </h2>
          <p className="mt-2 text-body text-text-primary">{t("links.text")}</p>
          <ul className="mt-3 flex flex-col gap-2 text-body">
            <li>
              <Link href="/cv" className="underline">
                {t("links.cvLink")}
              </Link>
            </li>
            <li>
              <Link href="/tips" className="underline">
                {t("links.tipsLink")}
              </Link>
            </li>
          </ul>
        </div>
      </div>
    </main>
  );
}
