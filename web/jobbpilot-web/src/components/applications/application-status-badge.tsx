import { cn } from "@/lib/utils";
import { getStatusLabel, STATUS_BADGE_VARIANT, type BadgeVariant } from "@/lib/applications/status";
import type { ApplicationStatus } from "@/lib/types/applications";

const VARIANT_CLASSES: Record<BadgeVariant, string> = {
  Info:    "bg-info-50 text-info-700",
  Brand:   "bg-brand-50 text-brand-700",
  Success: "bg-success-50 text-success-700",
  Warning: "bg-warning-50 text-warning-700",
  Danger:  "bg-danger-50 text-danger-700",
  Neutral: "bg-surface-tertiary text-text-secondary",
};

interface ApplicationStatusBadgeProps {
  status: ApplicationStatus;
  className?: string;
}

export function ApplicationStatusBadge({ status, className }: ApplicationStatusBadgeProps) {
  const variant = STATUS_BADGE_VARIANT[status];
  return (
    <span
      role="status"
      className={cn(
        "inline-flex items-center rounded-pill px-2 py-0.5 text-xs font-medium",
        VARIANT_CLASSES[variant],
        className
      )}
    >
      {getStatusLabel(status)}
    </span>
  );
}
