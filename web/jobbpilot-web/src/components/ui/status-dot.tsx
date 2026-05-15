import { cn } from "@/lib/utils";

/**
 * Ledger-stil statusindikator: färgad prick + textetikett, ingen bakgrund.
 * Förstaval för status i tabeller (civic-utility). Status kommuniceras
 * ALLTID med både färg och text — aldrig färg ensam (a11y).
 */
export type StatusTone =
  | "brand"
  | "info"
  | "success"
  | "warning"
  | "danger"
  | "neutral";

export function StatusDot({
  tone,
  children,
  className,
}: {
  tone: StatusTone;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <span className={cn("jp-statusDot", `jp-statusDot--${tone}`, className)}>
      <span className="jp-statusDot__d" aria-hidden="true" />
      {children}
    </span>
  );
}
