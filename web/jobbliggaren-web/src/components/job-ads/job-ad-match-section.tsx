import Link from "next/link";
import { useId } from "react";
import { useTranslations } from "next-intl";
import { ArrowRight, Check, CircleAlert, Info, Minus, X } from "lucide-react";
import { MatchChip } from "./match-chip";
import { MatchSkillOverflow } from "./match-skill-overflow";
import styles from "./job-ad-match-section.module.css";
import type { JobAdMatchDetail } from "@/lib/dto/job-ad-match";
import { useCodedTaxonomyName } from "@/lib/i18n/use-coded-taxonomy-name";
import { MATCH_SETTINGS_HREF } from "@/lib/nav/match-settings-href";
import type { OrtGranularity } from "@/lib/job-ads/ort-granularity";
import {
  buildMatchChecklist,
  overflowStart,
  type ChipTone,
  type DimensionRow,
  type MatchDimensionKey,
  type SkillChecklist,
  type SkillGroup,
} from "@/lib/job-ads/match-checklist";

// Scoped to the `match` subtree (next-intl typed-messages instantiate too
// deeply when ICU-arg calls resolve against the whole `jobads.ui` namespace).
type MatchTranslator = ReturnType<typeof useTranslations<"jobads.ui.match">>;

/**
 * JobAdMatchSection — the job card's match section as a checklist (#1963, ADR 0076 Amendment
 * 2026-10-03 (b), DESIGN.md §3 and §8). A Server Component shared by the modal and `/jobb/[id]`;
 * `match-checklist.ts` decides what is shown, this component only words and draws it.
 *
 * No percentage, gauge or ring (ADR 0053 Beslut 5, ADR 0076 Decision 4): a dimension shows a named
 * outcome and its evidence, and the skills counter counts the named chips beside it.
 */

function NoticeIcon() {
  return <Info size={16} className={styles.noticeIcon} aria-hidden="true" />;
}

function Notice({ children }: { children: React.ReactNode }) {
  return (
    <div className={styles.notice}>
      <NoticeIcon />
      <p className={styles.noticeText}>{children}</p>
    </div>
  );
}

function unnamedText(key: MatchDimensionKey, count: number, t: MatchTranslator): string | null {
  switch (key) {
    case "ssykOverlap":
      return t("unnamedEvidence.ssykOverlap", { count });
    case "regionFit":
      return t("unnamedEvidence.regionFit", { count });
    default:
      return null;
  }
}

function rowValue(row: DimensionRow, t: MatchTranslator): string | null {
  const value = row.value;
  switch (value.kind) {
    case "names":
      return value.names.join(", ");
    case "titleSummary":
      return t(`titleSummary.${value.verdict}`);
    case "unnamed":
      return unnamedText(row.key, value.count, t);
    case "cause":
      if (value.cause.dimension === "employmentFit") return t("matchCause.AdSilent.employmentFit");
      return value.cause.cause === "AdSilent"
        ? t("matchCause.AdSilent.regionFit")
        : t("matchCause.RemoteOverride.regionFit");
  }
}

function DimensionRows({ rows, t }: { rows: DimensionRow[]; t: MatchTranslator }) {
  if (rows.length === 0) return null;
  return (
    <ul className={styles.rows} role="list">
      {rows.map((row) => {
        const note = row.unnamedCount > 0 ? unnamedText(row.key, row.unnamedCount, t) : null;
        return (
          <li key={row.key} className={styles.row} data-tone={row.tone}>
            <span className={styles.rowIcon} aria-hidden="true">
              {row.tone === "match" ? <Check size={16} /> : <CircleAlert size={16} />}
            </span>
            <span className={styles.rowLabel}>{t(`dimension.${row.key}`)}</span>
            <span className={styles.rowValue}>
              {rowValue(row, t)}
              {note !== null && <span className={styles.rowNote}>{note}</span>}
            </span>
            <span className={styles.rowWord}>
              {row.word === "Related" ? t("ssyk.relatedWord") : t(`verdict.${row.word}`)}
            </span>
          </li>
        );
      })}
    </ul>
  );
}

function ChipIcon({ tone }: { tone: ChipTone }) {
  const Icon = tone === "met" ? Check : tone === "missingRequired" ? X : Minus;
  return <Icon size={14} className={styles.chipIcon} aria-hidden="true" />;
}

function groupHeading(
  kind: SkillGroup["kind"],
  hasRequirements: boolean,
  t: MatchTranslator,
): string {
  switch (kind) {
    case "must":
      return t("dimension.mustHaveCoverage");
    case "nice":
      return t("dimension.niceToHaveCoverage");
    case "profileMatched":
      return hasRequirements ? t("skills.profileMatchedAlso") : t("skills.profileMatched");
    case "profileMissing":
      return t("skills.profileMissing");
  }
}

function SkillGroupBlock({
  group,
  heading,
  baseId,
  t,
}: {
  group: SkillGroup;
  heading: string;
  baseId: string;
  t: MatchTranslator;
}) {
  const headingId = `${baseId}-${group.kind}`;
  const listId = `${headingId}-list`;
  const requirement = group.kind === "must" || group.kind === "nice";
  const start = group.kind === "profileMissing" ? overflowStart(group.chips.length) : null;

  const list = (
    <ul id={listId} className={styles.chips} role="list" aria-label={t("skills.listLabel", { group: heading })}>
      {group.chips.map((chip, index) => (
        <li
          key={chip.key}
          className={styles.chip}
          data-tone={chip.tone}
          data-overflow={start !== null && index >= start ? "" : undefined}
        >
          <ChipIcon tone={chip.tone} />
          {requirement ? (
            // A requirement list mixes met and missing chips; the icon is decorative, so the
            // status is spoken in words (WCAG 1.3.1).
            <>
              <span aria-hidden="true">{chip.label}</span>
              <span className="sr-only">
                {chip.tone === "met"
                  ? t("skills.metItem", { label: chip.label })
                  : t("skills.unmetItem", { label: chip.label })}
              </span>
            </>
          ) : (
            chip.label
          )}
        </li>
      ))}
    </ul>
  );

  return (
    <div className={styles.group}>
      <p id={headingId} className={styles.groupTitle}>
        {heading}
      </p>
      {start !== null ? (
        <MatchSkillOverflow
          listId={listId}
          describedById={headingId}
          moreLabel={t("skills.showMore", { count: group.chips.length - start })}
          lessLabel={t("skills.showLess")}
        >
          {list}
        </MatchSkillOverflow>
      ) : (
        <div className={styles.flow}>{list}</div>
      )}
    </div>
  );
}

function SkillsBox({ skills, t }: { skills: SkillChecklist; t: MatchTranslator }) {
  const baseId = useId();
  return (
    <div className={styles.skills}>
      <div className={styles.skillsHead}>
        <p className={styles.skillsTitle}>{t("dimension.skillOverlap")}</p>
        {skills.state === "assessed" && skills.counts !== null && (
          <p className={styles.counter}>
            {t("skills.count", {
              matched: skills.counts.matched,
              missing: skills.counts.missing,
            })}
          </p>
        )}
      </div>
      {skills.state === "notAssessed" ? (
        <Notice>{t("skillsFoot")}</Notice>
      ) : (
        <>
          {skills.groups.map((group) => (
            <SkillGroupBlock
              key={group.kind}
              group={group}
              heading={groupHeading(
                group.kind,
                skills.groups.some((g) => g.kind === "must" || g.kind === "nice"),
                t,
              )}
              baseId={baseId}
              t={t}
            />
          ))}
          {skills.requirementsNote !== null && (
            <Notice>{t(`requirementsEmpty.${skills.requirementsNote}`)}</Notice>
          )}
        </>
      )}
    </div>
  );
}

export interface JobAdMatchSectionProps {
  match: JobAdMatchDetail;
  /** conceptId → kommun/län from the taxonomy; omitted → Ort names keep the wire order. */
  ortGranularityByConceptId?: Record<string, OrtGranularity>;
}

export function JobAdMatchSection({ match, ortGranularityByConceptId }: JobAdMatchSectionProps) {
  // Synchronous next-intl translator — keeps JobAdMatchSection a non-async RSC (shared by the
  // modal + full page as a serialized slot, with sync tests).
  const t = useTranslations("jobads.ui.match");
  const codedName = useCodedTaxonomyName();
  const checklist = buildMatchChecklist(match, { codedName, ortGranularityByConceptId });

  if (checklist.kind === "noStatedOccupation") {
    // The card's only link to the matching settings: stating an occupation is the one thing the
    // reader can do here (#1963). The link soft-navigates and the `@modal` slot's null page closes
    // the modal on the way.
    return (
      <section className={styles.section} aria-label={t("heading")}>
        <div className={styles.head}>
          <div className="jp-eyebrow">{t("heading")}</div>
        </div>
        <div className={styles.setup}>
          <Info size={18} className={styles.noticeIcon} aria-hidden="true" />
          <div className={styles.setupBody}>
            <p className={styles.setupText}>{t("noStatedOccupation")}</p>
            <Link href={MATCH_SETTINGS_HREF} className="jp-btn jp-btn--sm jp-btn--info-outline">
              {t("settingsCtaNav")}
              <ArrowRight size={14} aria-hidden="true" />
            </Link>
          </div>
        </div>
      </section>
    );
  }

  return (
    <section className={styles.section} aria-label={t("heading")}>
      <div className={styles.head}>
        <div className="jp-eyebrow">{t("heading")}</div>
        {match.grade !== null && <MatchChip grade={match.grade} />}
      </div>
      {checklist.occupationSilent && (
        <p className={styles.silent}>{t("matchCause.AdSilent.ssykOverlap")}</p>
      )}
      <DimensionRows rows={checklist.rows} t={t} />
      <SkillsBox skills={checklist.skills} t={t} />
    </section>
  );
}
