"use client";

import { useId } from "react";
import { useTranslations } from "next-intl";
import { useMatchSkillAdd } from "./match-skill-add-context";
import styles from "./job-ad-match-section.module.css";

/**
 * #2247 — a missing skill as one button: "Lägg till <skill>". The accessible name is the visible
 * text (WCAG 2.5.3); the hover text says where it goes. `aria-disabled`, never `disabled`, while an
 * add runs, so focus stays on the chip the reader just pressed.
 *
 * Outside a {@link MatchSkillAddRegion} (no region rendered it) the chip stays plain text: a button
 * that cannot reach the backend is not offered.
 */
export function MatchSkillAddButton({
  chipKey,
  conceptIds,
  label,
  icon,
  status,
}: {
  chipKey: string;
  conceptIds: ReadonlyArray<string>;
  label: string;
  icon: React.ReactNode;
  /** Spoken status for a list that mixes met and missing chips ("Saknas: …", WCAG 1.3.1); else none. */
  status?: string;
}) {
  const t = useTranslations("jobads.ui.match.skills.add");
  const region = useMatchSkillAdd();
  const statusId = useId();

  if (region === null) {
    return (
      <>
        {icon}
        {label}
      </>
    );
  }

  const busy = region.pendingKey !== null;
  const mine = region.pendingKey === chipKey;

  return (
    <>
      <button
        type="button"
        className={styles.chipAdd}
        title={t("title", { label })}
        aria-describedby={status !== undefined ? statusId : undefined}
        aria-disabled={busy ? "true" : undefined}
        aria-busy={mine ? "true" : undefined}
        onClick={() => {
          if (!busy) region.add(chipKey, conceptIds, label);
        }}
      >
        {icon}
        <span className={styles.chipAddPrefix}>{mine ? t("pending") : t("prefix")}</span>{" "}
        <span className={styles.chipAddLabel}>{label}</span>
      </button>
      {status !== undefined && (
        <span id={statusId} className="sr-only">
          {status}
        </span>
      )}
    </>
  );
}
