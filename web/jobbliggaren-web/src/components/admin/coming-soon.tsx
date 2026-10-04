import { useTranslations } from "next-intl";

/**
 * The one visible "Kommer snart" line of an unbuilt region (ADR 0150 D2). The region keeps its
 * structure; its controls are natively disabled and point here with `aria-describedby`, so the
 * reason a control does nothing is read with the control.
 */
export function ComingSoon({
  id,
  region = false,
}: {
  readonly id?: string;
  /** Centred with room around it, for a region whose whole body is unbuilt. */
  readonly region?: boolean;
}) {
  const t = useTranslations("admin.unavailable");
  return (
    <p id={id} className={region ? "jp-adminsoon jp-adminsoon--region" : "jp-adminsoon"}>
      {t("comingSoon")}
    </p>
  );
}
