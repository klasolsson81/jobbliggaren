import type { ReactNode } from "react";
import Link from "next/link";
import { ArrowRight, type LucideIcon } from "lucide-react";

export type AdminCardSpan = 3 | 4 | 5 | 7 | 8;

interface AdminCardProps {
  /** `aria-labelledby` target — the card's own heading id. */
  readonly id: string;
  readonly title: string;
  /** Column span in the overview's 12-column grid; narrower viewports widen it in CSS. */
  readonly span: AdminCardSpan;
  readonly icon?: LucideIcon;
  /** The list cards: wider padding and a 20px heading, no icon box. */
  readonly list?: boolean;
  /**
   * The attention card's state. `unknown` keeps the left edge neutral, so a source that is not
   * built yet never reads as an alarm (ADR 0150 D2).
   */
  readonly attention?: "unknown";
  /** Right-hand slot in the head row: a link to the card's page, or a period group. */
  readonly aside?: ReactNode;
  readonly children: ReactNode;
}

/**
 * The card shell of the admin overview (ADR 0150 D1): a labelled `<section>` with an `h2`, so
 * heading navigation lands on the cards and nothing else. The form is the house card — surface,
 * hairline border, `--jp-r-lg`, no shadow — without /oversikt's axis tints.
 */
export function AdminCard({
  id,
  title,
  span,
  icon: Icon,
  list = false,
  attention,
  aside,
  children,
}: AdminCardProps) {
  const classes = ["jp-admincard"];
  if (list) classes.push("jp-admincard--list");
  if (attention !== undefined) classes.push("jp-admincard--attention");

  return (
    <section
      className={classes.join(" ")}
      data-span={span}
      data-state={attention}
      aria-labelledby={id}
    >
      <div className="jp-admincard__head">
        {Icon === undefined ? null : (
          <span className="jp-admincard__icon" aria-hidden="true">
            <Icon size={20} aria-hidden="true" />
          </span>
        )}
        <h2
          id={id}
          className={list ? "jp-admincard__title jp-admincard__title--lg" : "jp-admincard__title"}
        >
          {title}
        </h2>
        {aside}
      </div>
      {children}
    </section>
  );
}

/** The head row's link to the page that holds the card's subject ("Loggar →"). */
export function AdminCardLink({ href, label }: { readonly href: string; readonly label: string }) {
  return (
    <Link href={href} className="jp-admincard__link">
      {label}
      <ArrowRight size={14} aria-hidden="true" />
    </Link>
  );
}
