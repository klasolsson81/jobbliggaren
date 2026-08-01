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
  /**
   * #892 (CTO R1): true when the source ad is an Art. 17 tombstone. The row
   * then shows the applicant's preserved snapshot identity (or "Saknas"
   * without one) and must carry the removed-ad marker — restored identity
   * without a death signal would let a dead ad look alive. Derived
   * structurally from the wire's adStatus, never by matching a literal.
   */
  adRemoved: boolean;
};

export type MonthOption = { value: string; label: string };

const AF_MINIMUM = 6;

// Show the filter only once the list is long enough to be worth filtering.
const FILTER_THRESHOLD = 6;

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
  rows: readonly ActivityReportRow[];
  selectedMonth: string; // "YYYY-MM"
  monthLabel: string; // "maj 2026"
  monthOptions: readonly MonthOption[];
  afUrl: string;
}) {
  const t = useTranslations("aktivitetsrapport");
  const router = useRouter();
  const [query, setQuery] = useState("");
  const count = rows.length;
  const belowMinimum = count < AF_MINIMUM;
  const showFilter = count >= FILTER_THRESHOLD;

  const needle = query.trim().toLowerCase();
  const filtered =
    showFilter && needle
      ? rows.filter((r) =>
          [r.employer, r.title, r.location].some(
            (v) => v != null && v.toLowerCase().includes(needle),
          ),
        )
      : rows;

  // WCAG 2.1 SC 3.2.2 On Input (level A), technique H84. The picker used to
  // `router.push` straight out of `onChange`, and on Windows/Chrome a CLOSED
  // `<select>` commits every arrow key. The list is the last twelve months, so
  // arrowing from the newest option to the oldest is eleven steps — and it fired
  // eleven navigations and eleven fetches, one per keystroke, each one a change
  // of context the user never asked for. Type-ahead has the same shape, one
  // navigation per letter.
  //
  // So the value the control shows is local state now, and navigation is its own
  // act: Enter, or leaving the field. Klas chose this form over H84's canonical
  // "Visa" button on 2026-08-01, with the consequence stated — picking with the
  // mouse no longer navigates on the click, it navigates when focus leaves.
  const [pendingMonth, setPendingMonth] = useState(selectedMonth);
  const [syncedMonth, setSyncedMonth] = useState(selectedMonth);

  // Adjusting state when a prop changes, in React's documented render-phase form
  // rather than an effect. It is load-bearing, not tidiness: once the value is
  // local, a month arriving from the server (a committed navigation, the back
  // button, a bookmarked `?month=`) would otherwise leave the control showing one
  // month while the report below it lists another.
  if (selectedMonth !== syncedMonth) {
    setSyncedMonth(selectedMonth);
    setPendingMonth(selectedMonth);
  }

  function commitMonth(month: string) {
    // Guarded so leaving the field untouched is not a navigation. Without it,
    // every tab-through of the card would refetch the month already on screen.
    if (month === selectedMonth) return;
    router.push(`/aktivitetsrapport?month=${month}`);
  }

  function handleMonthKeyDown(event: React.KeyboardEvent<HTMLSelectElement>) {
    if (event.key !== "Enter") return;
    // There is no form here, so Enter has no default to speak of; prevented
    // anyway so wrapping this card in one later cannot turn it into a submit.
    event.preventDefault();
    commitMonth(pendingMonth);
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="jp-card flex flex-col gap-4">
        <div className="flex flex-col gap-1.5">
          <label
            htmlFor="aktivitetsrapport-month"
            className="text-label leading-5 font-medium text-text-primary"
          >
            {t("month.label")}
          </label>
          <select
            id="aktivitetsrapport-month"
            className="jp-input"
            value={pendingMonth}
            aria-describedby="aktivitetsrapport-month-hint"
            onChange={(event) => setPendingMonth(event.target.value)}
            onKeyDown={handleMonthKeyDown}
            onBlur={() => commitMonth(pendingMonth)}
          >
            {monthOptions.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
          {/* Navigation is no longer implied by the control's appearance, so it
              is said in words and tied to the select with aria-describedby. */}
          <p
            id="aktivitetsrapport-month-hint"
            className="text-body-sm leading-5 text-text-primary"
          >
            {t("month.hint")}
          </p>
        </div>

        <div className="flex flex-col gap-1">
          <p className="text-text-primary">
            {t("counter.text", { count, month: monthLabel })}
          </p>
          <p
            className={
              belowMinimum
                ? "text-body-sm leading-5 font-medium text-warning-600"
                : "text-body-sm leading-5 text-text-primary"
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
        <div className="flex flex-col gap-4">
          {showFilter ? (
            <div className="flex flex-col gap-1.5">
              <label
                htmlFor="aktivitetsrapport-filter"
                className="text-label leading-5 font-medium text-text-primary"
              >
                {t("filter.label")}
              </label>
              <input
                id="aktivitetsrapport-filter"
                type="search"
                className="jp-input"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
              />
            </div>
          ) : null}

          {filtered.length === 0 ? (
            <p className="text-text-primary">{t("filter.empty")}</p>
          ) : (
            <ol className="flex list-none flex-col gap-4 p-0">
              {filtered.map((row) => (
                <ApplicationCard key={row.applicationId} row={row} />
              ))}
            </ol>
          )}
        </div>
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

  const subtitle = [row.employer, row.location].filter(Boolean).join(" · ");

  return (
    <li className="overflow-hidden rounded-md border-2 border-border bg-surface-primary">
      {/* Banner header — the card's identity at a glance (Klas 2026-06-28). */}
      <div className="border-b-2 border-border bg-brand-50 px-5 py-3.5">
        <h2 className="text-h4 leading-6 font-bold wrap-break-word text-text-primary">
          {row.title ?? t("card.titleFallback")}
        </h2>
        {subtitle ? (
          <p className="mt-0.5 text-body-sm leading-5 wrap-break-word text-text-primary">
            {subtitle}
          </p>
        ) : null}
        {/* #892 (CTO R1): borttagen-markören — raden visar den bevarade
            kopians identitet och får inte se levande ut. */}
        {row.adRemoved ? (
          <p className="mt-1">
            <span className="jp-tag jp-tag--neutral">{t("card.adRemoved")}</span>
          </p>
        ) : null}
      </div>

      <div className="px-5 py-1">
        <CopyField label={t("fields.employer")} value={row.employer} />
        <CopyField label={t("fields.title")} value={row.title} />
        <CopyField label={t("fields.location")} value={row.location} />
        <CopyField label={t("fields.appliedAt")} value={row.appliedDate} />

        <div className="flex flex-col gap-1.5 border-t border-border py-3">
          <label
            htmlFor={`how-${row.applicationId}`}
            className="text-label leading-5 font-medium text-text-primary"
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
          <CopyField
            label={t("fields.link")}
            value={row.url}
            href={row.url}
          />
        ) : null}
      </div>
    </li>
  );
}

/**
 * A label + value row with its own copy button. When <paramref name="href"/> is
 * given the value renders as a link that opens the advert in a new tab (in
 * addition to the copy button). Empty values render a neutral "Saknas"
 * placeholder with no button (we never copy nothing, and never surface an
 * unavailable field as if it had data).
 */
function CopyField({
  label,
  value,
  href,
}: {
  label: string;
  value: string | null;
  href?: string;
}) {
  const t = useTranslations("aktivitetsrapport");
  return (
    <div className="flex items-center justify-between gap-3 border-t border-border py-3">
      <div className="flex min-w-0 flex-col gap-0.5">
        <span className="text-label leading-5 font-medium text-text-secondary">{label}</span>
        {value && href ? (
          <a
            href={href}
            target="_blank"
            rel="noopener noreferrer"
            aria-label={t("fields.linkOpen")}
            className="break-all underline underline-offset-2"
          >
            {value}
          </a>
        ) : (
          <span
            className={
              value
                ? "wrap-break-word text-text-primary"
                : "wrap-break-word text-text-secondary"
            }
          >
            {value ?? t("fields.empty")}
          </span>
        )}
      </div>
      {value ? <CopyButton value={value} fieldLabel={label} /> : null}
    </div>
  );
}
