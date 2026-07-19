// GUEST-ONLY (efter #726 notiscenter-refaktoreringen): den autentiserade
// `/oversikt` har inte längre en Sammanfattning. Denna rad-komponent lever kvar
// enbart för den publika gäst-förhandsvisningen (`guest-oversikt-page.tsx`, som
// bygger en egen sammanfattnings-tabell), en avsiktlig demo-yta.
import Link from "next/link";
import { ChevronRight } from "lucide-react";

interface SummaryRowProps {
  readonly label: string;
  readonly value: string | number;
  readonly href?: string;
  readonly highlight?: boolean;
  readonly hint?: string;
}

/**
 * Sammanfattning-rad. Server Component — render-only.
 *
 * Per HANDOVER §6 a11y-not: `<Link>` om klickbar (semantisk navigation),
 * `<div>` annars. Chevron-slot reserveras alltid via `visibility:hidden`
 * så värdekolumnen alignar mellan klickbara/icke-klickbara rader.
 */
export function SummaryRow({
  label,
  value,
  href,
  highlight,
  hint,
}: SummaryRowProps) {
  const className =
    "jp-summary__row" +
    (href ? " jp-summary__row--btn" : "") +
    (highlight ? " jp-summary__row--highlight" : "");

  const content = (
    <>
      <span className="jp-summary__row__label">
        {label}
        {hint && <span className="jp-summary__row__hint"> · {hint}</span>}
      </span>
      <span className="jp-summary__row__leader" aria-hidden="true" />
      <span className="jp-summary__row__value">{value}</span>
      <ChevronRight
        size={14}
        className="jp-summary__row__chev"
        style={href ? undefined : { visibility: "hidden" }}
        aria-hidden="true"
      />
    </>
  );

  if (href) {
    return (
      <Link href={href} className={className}>
        {content}
      </Link>
    );
  }
  return <div className={className}>{content}</div>;
}
