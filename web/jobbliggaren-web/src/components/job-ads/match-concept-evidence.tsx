import { Check, Minus } from "lucide-react";
import { useTranslations } from "next-intl";
import type { MatchConceptGroup, MatchSkillDimensionDetail } from "@/lib/dto/job-ad-match";
import styles from "./match-concept-evidence.module.css";

export function MatchConceptEvidence({ detail, dimension }: {
  detail: MatchSkillDimensionDetail;
  dimension: string;
}) {
  const t = useTranslations("jobads.ui.match");
  return (
    <div className={styles.evidence}>
      {(["matched", "missing"] as const).map((side) => {
        // Old APIs have display-only evidence. Keep every entry, including duplicates.
        const groups: MatchConceptGroup[] = detail.conceptEvidence?.[side]
          ?? detail[side].map((display) => ({ display, members: [] }));
        if (groups.length === 0) return null;
        const heading = t(`evidence.${side}`);
        return (
          <div key={side}>
            <p className={styles.heading}>{heading}</p>
            <ul className={styles.list} aria-label={`${dimension}: ${heading}`}>
              {groups.map((group, index) => {
                const aliases = [...new Set(group.members.map((member) => member.display))]
                  .filter((display) => display !== group.display);
                return (
                  <li key={group.members[0]?.conceptId ?? `${side}-${index}`}
                    className={`${styles.item} ${side === "missing" ? styles.missing : ""}`}>
                    {side === "matched"
                      ? <Check size={16} aria-hidden="true" className={styles.matchedIcon} />
                      : <Minus size={16} aria-hidden="true" className={styles.missingIcon} />}
                    <div className={styles.words}>
                      <span>{group.display}</span>
                      {aliases.length > 0 && <span className={styles.aliases}>({aliases.join(", ")})</span>}
                    </div>
                  </li>
                );
              })}
            </ul>
          </div>
        );
      })}
    </div>
  );
}
