"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { ExternalLink } from "lucide-react";
import { CopyButton } from "./copy-button";

/** One application row, pre-projected by the RSC (dates already formatted). */
export type ActivityReportRow = {
  applicationId: string;
  appliedDate: string; // YYYY-MM-DD (Europe/Stockholm), form-ready + copyable
  employer: string | null;
  title: string | null;
  location: string | null;
  source: string | null; // "Platsbanken" | "LinkedIn" | "Manual" | null
  url: string | null;
};

export type MonthOption = { value: string; label: string };

const AF_MINIMUM = 6;

/**
 * AF activity-report helper view (issue #316). Lists the month's applications,
 * one card per sought job, with a per-field copy button so the user fills
 * Arbetsförmedlingen's per-field form by copying field by field — never a text
 * block (which flags the report for manual review).
 */
export function ActivityReportView({
  rows,
  selectedMonth,
  monthLabel,
  monthOptions,
  afUrl,
}: {
  rows: ActivityReportRow[];
  selectedMonth: string; // "YYYY-MM"
  monthLabel: string; // "maj 2026"
  monthOptions: MonthOption[];
  afUrl: string;
}) {
  const t = useTranslations("aktivitetsrapport");
  const router = useRouter();
  const count = rows.length;
  const belowMinimum = count < AF_MINIMUM;

  function handleMonthChange(event: React.ChangeEvent<HTMLSelectElement>) {
    router.push(`/ansokningar/aktivitetsrapport?month=${event.target.value}`);
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="jp-card flex flex-col gap-4">
        <div className="flex flex-col gap-1.5">
          <label
            htmlFor="aktivitetsrapport-month"
            className="text-sm font-medium text-text-secondary"
          >
            {t("month.label")}
          </label>
          <select
            id="aktivitetsrapport-month"
            className="jp-input"
            value={selectedMonth}
            onChange={handleMonthChange}
          >
            {monthOptions.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>

        <div className="flex flex-col gap-1">
          <p className="text-text-primary">
            {t("counter.text", { count, month: monthLabel })}
          </p>
          <p
            className={
              belowMinimum
                ? "text-sm font-medium text-warning-600"
                : "text-sm text-text-secondary"
            }
          >
            {t("counter.minimum", { minimum: AF_MINIMUM })}
          </p>
        </div>

        <a
          href={afUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="jp-btn jp-btn--primary self-start"
        >
          {t("cta")}
          <ExternalLink size={16} aria-hidden="true" />
        </a>
      </div>

      {count === 0 ? (
        <div className="jp-card">
          <p className="text-text-primary">
            {t("empty.text", { month: monthLabel })}
          </p>
        </div>
      ) : (
        <ol className="flex list-none flex-col gap-4 p-0">
          {rows.map((row) => (
            <ApplicationCard key={row.applicationId} row={row} />
          ))}
        </ol>
      )}
    </div>
  );
}

function ApplicationCard({ row }: { row: ActivityReportRow }) {
  const t = useTranslations("aktivitetsrapport");

  // "Hur du sökte" default derives from the source; editable, never stored.
  const howAppliedDefault =
    row.source === "Platsbanken"
      ? t("howApplied.platsbanken")
      : row.source === "LinkedIn"
        ? t("howApplied.linkedin")
        : t("howApplied.other");
  const [howApplied, setHowApplied] = useState(howAppliedDefault);

  return (
    <li className="jp-card flex flex-col gap-0">
      <CopyField label={t("fields.employer")} value={row.employer} />
      <CopyField label={t("fields.title")} value={row.title} />
      <CopyField label={t("fields.location")} value={row.location} />
      <CopyField label={t("fields.appliedAt")} value={row.appliedDate} />

      <div className="flex flex-col gap-1.5 border-t border-border py-3 first:pt-0">
        <label
          htmlFor={`how-${row.applicationId}`}
          className="text-sm font-medium text-text-secondary"
        >
          {t("fields.howApplied")}
        </label>
        <div className="flex items-center gap-2">
          <input
            id={`how-${row.applicationId}`}
            className="jp-input flex-1"
            value={howApplied}
            onChange={(event) => setHowApplied(event.target.value)}
          />
          <CopyButton value={howApplied} fieldLabel={t("fields.howApplied")} />
        </div>
      </div>

      {row.url ? (
        <CopyField label={t("fields.link")} value={row.url} />
      ) : null}
    </li>
  );
}

/**
 * A label + value row with its own copy button. Empty values render a neutral
 * "—" with no button (we never copy nothing, and never surface an unavailable
 * field as if it had data).
 */
function CopyField({
  label,
  value,
}: {
  label: string;
  value: string | null;
}) {
  const t = useTranslations("aktivitetsrapport");
  return (
    <div className="flex items-center justify-between gap-3 border-t border-border py-3 first:border-t-0 first:pt-0">
      <div className="flex min-w-0 flex-col gap-0.5">
        <span className="text-sm font-medium text-text-secondary">{label}</span>
        <span
          className={
            value
              ? "break-words text-text-primary"
              : "break-words text-text-secondary"
          }
        >
          {value ?? t("fields.empty")}
        </span>
      </div>
      {value ? <CopyButton value={value} fieldLabel={label} /> : null}
    </div>
  );
}
