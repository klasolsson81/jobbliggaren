import { useTranslations } from "next-intl";
import type { AdminRegionKind } from "@/lib/admin/view-models";
import { ComingSoon } from "./coming-soon";

export type AdminRegionLineKind = Exclude<AdminRegionKind, "loaded">;

/**
 * The one line a region shows in place of its content (ADR 0150 D2): "Kommer snart" while no
 * source exists, the region's own empty sentence, a failure as an alert, or loading as a status.
 * Failure and loading fall back to the surface's shared sentences.
 */
export function AdminRegionLine({
  kind,
  empty,
  failed,
  loading,
  soonId,
  region = false,
  quiet = false,
  className,
}: {
  readonly kind: AdminRegionLineKind;
  /** Required where the region can be empty; a value region never is. */
  readonly empty?: string;
  readonly failed?: string;
  readonly loading?: string;
  /** The "Kommer snart" line's id, for disabled controls to point to. */
  readonly soonId?: string;
  /** Centred with room around it, for a region whose whole body is the line. */
  readonly region?: boolean;
  /** Replaces the line's own class, for a line that sits in a card's sub row. */
  readonly className?: string;
  /** Leaves failure and loading unannounced, for a page that announces them once for all its regions. */
  readonly quiet?: boolean;
}) {
  const t = useTranslations("admin");
  const lineClass = className ?? (region ? "jp-adminsoon jp-adminsoon--region" : "jp-adminsoon");
  switch (kind) {
    case "unavailable":
      return className === undefined ? (
        <ComingSoon id={soonId} region={region} />
      ) : (
        <p id={soonId} className={className}>
          {t("unavailable.comingSoon")}
        </p>
      );
    case "empty":
      return <p className={lineClass}>{empty}</p>;
    case "failed":
      return (
        <p className={lineClass} role={quiet ? undefined : "alert"}>
          {failed ?? t("regions.failed")}
        </p>
      );
    case "loading":
      return (
        <p className={lineClass} role={quiet ? undefined : "status"}>
          {loading ?? t("regions.loading")}
        </p>
      );
  }
}
