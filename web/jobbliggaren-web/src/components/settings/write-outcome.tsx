import { useFormatter, useTranslations } from "next-intl";
import { formatTime } from "@/lib/i18n/format";

/** The outcome of one write, owned by the control that started it (#1391). */
export type WriteOutcome = { ok: true; at: Date } | { ok: false; error: string };

/**
 * The receipt or the refusal under one control. Mutually exclusive live regions, the shape the
 * settings cards have shipped since #1391: a refusal is an assertive alert, otherwise a polite status
 * that stays mounted so a later receipt is announced.
 */
export function Outcome({ id, outcome }: { id: string; outcome: WriteOutcome | null }) {
  const t = useTranslations("settings");
  const format = useFormatter();
  if (outcome?.ok === false) {
    return (
      <p id={id} role="alert" className="jp-settings-group__message text-body-sm text-danger-600">
        {outcome.error}
      </p>
    );
  }
  return (
    <p
      role="status"
      aria-live="polite"
      className="jp-settings-group__message text-body-sm text-text-secondary"
    >
      {outcome?.ok ? t("savedAt", { time: formatTime(format, outcome.at) }) : ""}
    </p>
  );
}
