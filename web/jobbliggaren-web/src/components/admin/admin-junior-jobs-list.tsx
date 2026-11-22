"use client";

// "use client": each row runs its decision in a transition and shows its own refusal, and the list moves focus and
// announces when a decided row leaves it. Every string arrives as a prop: the admin layout's client messages carry
// no junior namespace (its document budget), so nothing here reads a translation.
import Link from "next/link";
import { useCallback, useEffect, useRef, useState, useTransition, type ReactNode, type RefCallback } from "react";
import { ArrowLeft, ArrowRight, MapPin } from "lucide-react";
import { RemovalStatus, useRowRemoval, type RowRemoval } from "@/components/common/row-removal";
import type { ActionResult } from "@/lib/actions/_action-result";
import type { AdminJuniorChoice } from "@/lib/admin/junior-jobs";
import { AdminBusyLabel } from "./admin-busy-label";

/** One button: what it asks for, its label, and what the list announces once the decision went through. */
export interface AdminJuniorChoiceView {
  readonly decision: AdminJuniorChoice;
  readonly label: string;
  readonly receipt: string;
}

/** One ad as the row shows it, every value already in words. */
export interface AdminJuniorRowView {
  readonly id: string;
  readonly title: string;
  readonly company: string;
  readonly municipality: string | null;
  readonly published: string | null;
  readonly expires: string | null;
  /** The rule that placed the ad, shown while the rules decide. */
  readonly reason: string;
  /**
   * On a decided row: what the rules came to ("Oavgjord · Juniorord utan avgörande regel"), which is what a hand-back
   * returns the ad to, and the decision that outranks it ("Junior"). Null while the rules decide.
   */
  readonly decided: { readonly rules: string; readonly decision: string } | null;
  readonly choices: ReadonlyArray<AdminJuniorChoiceView>;
}

export interface AdminJuniorListLabels {
  readonly municipality: string;
  readonly published: string;
  readonly expires: string;
  readonly reason: string;
  readonly rules: string;
  readonly decision: string;
  readonly busy: string;
}

interface PagerLinkView {
  readonly href: string;
  readonly label: string;
  readonly disabled: boolean;
}

export interface AdminJuniorPagerView {
  readonly label: string;
  readonly position: string;
  readonly previous: PagerLinkView;
  readonly next: PagerLinkView;
}

export interface AdminJuniorJobsListProps {
  readonly rows: ReadonlyArray<AdminJuniorRowView>;
  /** What stands in the list's place when the page has no rows. */
  readonly empty: ReactNode;
  readonly pager: AdminJuniorPagerView | null;
  readonly labels: AdminJuniorListLabels;
  /** The page's h1, which takes focus when the last row leaves. It carries `tabIndex={-1}`. */
  readonly headingId: string;
  readonly onDecide: (id: string, decision: AdminJuniorChoice) => Promise<ActionResult>;
}

/**
 * The rows of one tab. A decision that moves a row out of its tab takes the row away when the page has been read
 * again; focus then goes to the next row's title, else the previous row's, else the h1, and the list's status says
 * what was decided (DESIGN.md §6). The status region stays when the list empties, so the last receipt is heard.
 */
export function AdminJuniorJobsList({ rows, empty, pager, labels, headingId, onDecide }: AdminJuniorJobsListProps) {
  const removal = useRowRemoval(
    rows.map((row) => row.id),
    () => document.getElementById(headingId),
  );

  return (
    <>
      <RemovalStatus receipt={removal.receipt} />
      {rows.length === 0 ? (
        empty
      ) : (
        <ol className="jp-adminjunior__list">
          {rows.map((row) => (
            <JuniorRow
              key={row.id}
              row={row}
              labels={labels}
              firstStopRef={removal.firstStopRef(row.id)}
              removal={removal}
              onDecide={onDecide}
            />
          ))}
        </ol>
      )}
      {pager === null ? null : <JuniorPager pager={pager} />}
    </>
  );
}

function JuniorRow({
  row,
  labels,
  firstStopRef,
  removal,
  onDecide,
}: {
  readonly row: AdminJuniorRowView;
  readonly labels: AdminJuniorListLabels;
  readonly firstStopRef: RefCallback<HTMLElement>;
  readonly removal: RowRemoval;
  readonly onDecide: AdminJuniorJobsListProps["onDecide"];
}) {
  const { started, failed, announce } = removal;
  const [pending, startTransition] = useTransition();
  const [running, setRunning] = useState<number | null>(null);
  const [refusal, setRefusal] = useState<string | null>(null);
  const choicesRef = useRef<HTMLDivElement>(null);
  const titleId = `junior-${row.id}-title`;

  // What the row offers. A decision that keeps the row in its tab changes it: a decided row's decision flips, and
  // one handed back to a rule that shows it loses its hand-back button.
  const shape = `${row.decided?.decision ?? ""}|${row.choices.map((choice) => String(choice.decision)).join(",")}`;
  const shown = useRef(shape);
  /** The shape the row had when its decision went through, until the page read after it arrives. */
  const awaiting = useRef<string | null>(null);

  // The row stayed: its leaving is no longer expected, and a button that left with the old decision has dropped
  // focus, which goes to the row's first button.
  const stayed = useCallback(() => {
    awaiting.current = null;
    failed(row.id);
    const active = document.activeElement;
    if (active === null || active === document.body) choicesRef.current?.querySelector("button")?.focus();
  }, [failed, row.id]);

  useEffect(() => {
    shown.current = shape;
    if (awaiting.current !== null && awaiting.current !== shape) stayed();
  }, [shape, stayed]);

  // The buttons stay focusable while the decision runs (DESIGN.md §6): a disabled button holding focus drops it to
  // the page. This guard is what prevents a second decision. A decision that throws ends at the error boundary.
  function decide(index: number, choice: AdminJuniorChoiceView) {
    if (pending) return;
    const before = shape;
    setRefusal(null);
    setRunning(index);
    started(row.id);
    startTransition(async () => {
      const result = await onDecide(row.id, choice.decision);
      startTransition(() => {
        setRunning(null);
        if (result.success) {
          announce(choice.receipt);
          // The page read after the decision may already be on screen; otherwise the row waits for it.
          if (shown.current !== before) stayed();
          else awaiting.current = before;
        } else {
          failed(row.id);
          setRefusal(result.error);
        }
      });
    });
  }

  return (
    <li className="jp-adminjunior__row" data-junior-row={row.id}>
      <div className="jp-adminjunior__main">
        <h3 id={titleId} className="jp-adminjunior__title">
          {/* A plain link: the ad is opened to be read, and nothing is fetched before it is. */}
          <a ref={firstStopRef} href={`/jobb/${row.id}`}>
            {row.title}
          </a>
        </h3>
        <p className="jp-adminjunior__employer">{row.company}</p>
        <dl className="jp-adminjunior__facts">
          {row.municipality === null ? null : (
            <div>
              {/* The pin names the axis on screen, as /junior's list does; the term names it to a screen reader. */}
              <dt className="sr-only">{labels.municipality}</dt>
              <dd className="jp-adminjunior__place">
                <MapPin size={14} aria-hidden="true" />
                {row.municipality}
              </dd>
            </div>
          )}
          {row.published === null ? null : (
            <div>
              <dt>{labels.published}</dt>
              <dd>{row.published}</dd>
            </div>
          )}
          {row.expires === null ? null : (
            <div>
              <dt>{labels.expires}</dt>
              <dd>{row.expires}</dd>
            </div>
          )}
          {row.decided === null ? (
            <div>
              <dt>{labels.reason}</dt>
              <dd>{row.reason}</dd>
            </div>
          ) : (
            <>
              <div>
                <dt>{labels.rules}</dt>
                <dd>{row.decided.rules}</dd>
              </div>
              <div>
                <dt>{labels.decision}</dt>
                <dd className="jp-adminjunior__decision">{row.decided.decision}</dd>
              </div>
            </>
          )}
        </dl>
      </div>
      <div ref={choicesRef} role="group" aria-labelledby={titleId} className="jp-adminjunior__choices">
        {row.choices.map((choice, index) => (
          // Keyed by place: a decision that changes keeps the pressed button, now offering the other one.
          <button
            key={index}
            type="button"
            className="jp-btn jp-btn--sm jp-btn--secondary"
            aria-disabled={pending || undefined}
            onClick={() => decide(index, choice)}
          >
            <AdminBusyLabel busy={running === index} label={choice.label} busyLabel={labels.busy} />
          </button>
        ))}
      </div>
      {refusal === null ? null : (
        <p role="alert" className="jp-adminjunior__refusal">
          {refusal}
        </p>
      )}
    </li>
  );
}

/** "Sida 2 av 3" with Föregående and Nästa on every page, in the same place; at a bound the link stays, inert. */
function JuniorPager({ pager }: { readonly pager: AdminJuniorPagerView }) {
  return (
    <nav className="jp-adminpager" aria-label={pager.label}>
      <p className="jp-adminpager__position" role="status">
        {pager.position}
      </p>
      <div className="jp-adminpager__buttons">
        <PagerLink link={pager.previous} rel="prev">
          <ArrowLeft size={16} aria-hidden="true" />
          {pager.previous.label}
        </PagerLink>
        <PagerLink link={pager.next} rel="next">
          {pager.next.label}
          <ArrowRight size={16} aria-hidden="true" />
        </PagerLink>
      </div>
    </nav>
  );
}

function PagerLink({
  link,
  rel,
  children,
}: {
  readonly link: PagerLinkView;
  readonly rel: "prev" | "next";
  readonly children: ReactNode;
}) {
  return (
    <Link
      href={link.href}
      scroll={false}
      rel={link.disabled ? undefined : rel}
      prefetch={link.disabled ? false : undefined}
      aria-disabled={link.disabled || undefined}
      className="jp-btn jp-btn--sm jp-btn--secondary"
      onClick={(event) => {
        if (link.disabled) event.preventDefault();
      }}
    >
      {children}
    </Link>
  );
}
