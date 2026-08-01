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
  const [announcement, setAnnouncement] = useState("");

  // Adjusting state when a prop changes, in React's documented render-phase form
  // rather than an effect. It is load-bearing, not tidiness: once the value is
  // local, a month arriving from the server (a committed navigation, the back
  // button, a bookmarked `?month=`) would otherwise leave the control showing one
  // month while the report below it lists another.
  //
  // It is also the honest anchor for the announcement: this branch fires when the
  // new month ARRIVED, not when the navigation started, and it does not fire on
  // first mount because `syncedMonth` initialises to `selectedMonth`. Back button
  // and deep link come along for free.
  if (selectedMonth !== syncedMonth) {
    setSyncedMonth(selectedMonth);
    setPendingMonth(selectedMonth);
    setAnnouncement(t("month.announced", { month: monthLabel }));
  }

  // The draft the user is holding, and the reason the two lines below exist. Once
  // the commit stopped being implicit, everything rests on saying so — and a
  // static sentence describing what WILL happen can never say that you are
  // standing in it right now.
  const monthDraftPending = pendingMonth !== selectedMonth;

  function commitMonth(month: string) {
    // Guarded so leaving the field untouched is not a navigation. Without it,
    // every tab-through of the card would refetch the month already on screen.
    if (month === selectedMonth) return;
    router.push(`/aktivitetsrapport?month=${month}`);
  }

  function handleMonthKeyDown(event: React.KeyboardEvent<HTMLSelectElement>) {
    if (event.key !== "Enter") return;
    // No preventDefault. An earlier version called it here as insurance against
    // a <form> that does not exist, and code-reviewer measured the price of that
    // insurance: Firefox dispatches keydown to a <select> while its native popup
    // is open (Chrome does not), so suppressing Enter's default there can
    // suppress the popup's own "commit the highlighted option" — on the very key
    // this feature now hangs on. There is no form to submit today, so there is no
    // default to prevent; the change that adds one is the change that carries its
    // own guard.
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
            // The hint is unconditional; the pending line joins the description
            // only while it carries text, or an empty description would be
            // announced as part of the field forever (the form
            // `foretag-sok-searchbar` settled on in its own round 2).
            aria-describedby={
              monthDraftPending
                ? "aktivitetsrapport-month-hint aktivitetsrapport-month-pending"
                : "aktivitetsrapport-month-hint"
            }
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
          {/* Draft-vs-applied honesty, inside the field it is about. The hint
              above says what WILL happen and is equally true before and after the
              act, so it can never say "you are standing in it now" — that is this
              line's whole job, and without it the control can read "april 2026"
              while the counter and the cards below say maj.

              ALWAYS rendered with its height reserved (`min-h-10 sm:min-h-5`,
              measured below), never
              conditionally mounted: toggling the node would shift the counter, the
              CTA and every card under it. `/foretag/sok` measured that same defect
              at 26 px and paid the same permanent cost for the same reason.

              NO `aria-live`, deliberately, and for the reason written out on that
              surface: this is a standing STATE, not an event. A live region here
              would announce on every arrow key — the same defect shape this PR
              removes. `aria-describedby` is the mechanism for a standing
              description. */}
          {/* Two lines reserved below `sm`, one at and above it. Measured in
              Chromium against the production build, six viewports: at 375 and up
              the sentence is one line and the card does not move at all, but at
              320 it wraps and a single reserved line let it push the CTA and the
              whole card list down 20 px — the exact reflow the reservation exists
              to prevent, surviving in the one viewport nobody looks at. */}
          <p
            id="aktivitetsrapport-month-pending"
            className="min-h-10 text-body-sm leading-5 text-text-primary sm:min-h-5"
          >
            {monthDraftPending ? t("month.pending", { month: monthLabel }) : ""}
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

      {/* Says that the month CHANGED. Next's route announcer is keyed on
          pathname, so a `?month=` swap never reaches it, and this PR widens the
          gap on purpose: the commit now happens AFTER focus has left the picker,
          so a screen-reader user is standing on the next control when everything
          below it is replaced in silence. Copying one FIELD already announces
          (`copy-button.tsx`); replacing the whole report did not.

          Persistent and mounted empty at first paint — a live region that appears
          together with its content is the trap that makes announcements
          unreliable. It carries the MONTH and never the count: a screen reader
          would otherwise hear a number for a list it has not reached yet. */}
      <p
        id="aktivitetsrapport-month-announcer"
        role="status"
        aria-live="polite"
        className="sr-only"
      >
        {announcement}
      </p>
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
