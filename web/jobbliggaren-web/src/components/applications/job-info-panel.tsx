"use client";

import { useId, useState } from "react";
import { useFormatter, useTranslations } from "next-intl";
import {
  applicationSourceLabel,
} from "@/lib/applications/status";
import { formatDate } from "@/lib/i18n/format";
import type { JobAdSummaryDto } from "@/lib/types/applications";

interface JobInfoPanelProps {
  jobAd: JobAdSummaryDto;
  coverLetter: string | null;
}

/**
 * Read-only TLDR-panel för den kopplade/manuella annonsen (vänster i
 * split-layout, §3). Renderas bara när jobAd != null — vid null ersätts
 * positionen av civic-not i page.tsx (L6, single-column). "Publicerad"-
 * raden utelämnas HELT när publishedAt == null (J1, manuell ansökan —
 * ingen CreatedAt-som-Publicerad-läcka). Sektionskort: border, aldrig
 * shadow (regel 1 papper-ej-glas).
 */
export function JobInfoPanel({ jobAd, coverLetter }: JobInfoPanelProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const format = useFormatter();
  const [open, setOpen] = useState(false);
  const disclosureId = useId();

  const published = formatDate(format, jobAd.publishedAt);
  const expires = formatDate(format, jobAd.expiresAt);
  const sourceLabel = applicationSourceLabel(t, jobAd.source);

  return (
    <section
      aria-labelledby="job-info-title"
      className="rounded-md border border-border-structural bg-surface-primary"
    >
      <div className="border-b border-border-default px-4 py-3">
        <h2
          id="job-info-title"
          className="text-h3 font-semibold text-text-primary"
        >
          {tUi("jobInfo.title")}
        </h2>
      </div>

      <div className="px-4 py-4">
        <dl className="flex flex-col gap-3 text-body-sm">
          <div className="flex flex-col gap-0.5">
            <dt className="text-text-secondary">{tUi("jobInfo.company")}</dt>
            <dd className="text-text-primary">{jobAd.company}</dd>
          </div>

          {published && (
            <div className="flex flex-col gap-0.5">
              <dt className="text-text-secondary">{tUi("jobInfo.published")}</dt>
              <dd className="font-mono text-text-primary">{published}</dd>
            </div>
          )}

          <div className="flex flex-col gap-0.5">
            <dt className="text-text-secondary">{tUi("jobInfo.applyBy")}</dt>
            <dd className="font-mono text-text-primary">{expires ?? "–"}</dd>
          </div>

          <div className="flex flex-col gap-0.5">
            <dt className="text-text-secondary">{tUi("jobInfo.source")}</dt>
            <dd className="text-text-primary">{sourceLabel}</dd>
          </div>
        </dl>

        {jobAd.url && (
          <p className="mt-4">
            <a
              href={jobAd.url}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={tUi("jobInfo.viewAdAriaLabel", { source: sourceLabel })}
              className="text-body-sm text-brand-600 underline-offset-2 hover:underline"
            >
              {tUi("jobInfo.viewAd")}{" "}
              <span aria-hidden="true">↗</span>
            </a>
          </p>
        )}
      </div>

      {coverLetter && (
        <div className="border-t border-border-default px-4 py-3">
          <button
            type="button"
            aria-expanded={open}
            aria-controls={disclosureId}
            onClick={() => setOpen((v) => !v)}
            className="flex w-full items-center justify-between gap-2 rounded-sm py-1 text-left text-body-sm font-medium text-text-primary"
          >
            <span>{tUi("jobInfo.coverLetterLabel")}</span>
            <span aria-hidden="true" className="text-text-secondary">
              {open ? tUi("jobInfo.hide") : tUi("jobInfo.show")}
            </span>
          </button>
          {open && (
            <p
              id={disclosureId}
              className="mt-2 max-w-[68ch] whitespace-pre-wrap text-body text-text-primary"
            >
              {coverLetter}
            </p>
          )}
        </div>
      )}
    </section>
  );
}
