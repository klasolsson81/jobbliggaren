import { cn } from "@/lib/utils";

/**
 * Status-pill: färgad 50-bakgrund + prick + text. Används för accent vid
 * entitet (t.ex. "Standard"-CV, statusflagga på en rad). När bara status i
 * en tabellcell behövs — använd StatusDot istället (ingen bg).
 */
export type PillTone =
  | "neutral"
  | "info"
  | "brand"
  | "success"
  | "warning"
  | "danger";

export function StatusPill({
  tone = "neutral",
  dot = true,
  children,
  className,
}: {
  tone?: PillTone;
  dot?: boolean;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <span className={cn("jp-pill", `jp-pill--${tone}`, className)}>
      {dot && <span className="jp-pill__dot" aria-hidden="true" />}
      {children}
    </span>
  );
}
