"use client";

import { useEffect, useId, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { CheckBox } from "./criterion-tree";
import { cn } from "@/lib/utils";
import { groupTriState } from "@/lib/company-criteria/criterion-selection";
import type { CriterionOption } from "@/lib/company-criteria/criterion-options";
import type {
  DivisionShare,
  OccupationDivisionCandidate,
  OccupationDivisions,
} from "@/lib/dto/company-criteria";

/**
 * #1682 — the bransch picker's answer to an occupation word: "X är ett yrke, inte en bransch", then
 * where X's employers actually are, counted from our own ads, as checkable huvudgrupper. Ticking one
 * calls the picker's own `onToggle(leafCodes)` — exactly what picking it from the tree does, so no
 * new criterion axis exists and the saved watch stays SNI-based.
 *
 * Four properties keep this a measurement and not the authored crosswalk #560 bind 4 forbids, and
 * they are structural here, not policed: it never preselects (the user ticks), the count always
 * travels beside the share, it never collapses to a single division, and every ad is accounted for
 * on the surface — the rows, a line for the huvudgrupper under the share cut, and a line for the
 * employers with no huvudgrupp in the register — so the distribution cannot read as complete while
 * a third of it is missing.
 *
 * Several occupation groups for one word are a CHOICE the user makes first (AGENTS.md §5 — SSYK
 * derivation without user confirmation; ADR 0040 Beslut 4): the group names are listed, one is
 * picked, its map renders. One group skips the step. The choice is this component's own state, and
 * the picker mounts the block under `key={word}`, so a new word is a new block with no choice — the
 * reset is structural, not an effect. The floor and the share threshold are the server's; this
 * component renders the three states it is given and never a zero for "unknown".
 *
 * Not a live region, deliberately: the picker's one filter region names this block when it answers,
 * and this block appears and disappears with the typed word like the rows do.
 */
export interface OccupationDivisionBlockProps {
  readonly data: OccupationDivisions;
  readonly options: ReadonlyArray<CriterionOption>;
  readonly selected: ReadonlySet<string>;
  readonly onToggle: (leafCodes: ReadonlyArray<string>) => void;
}

type T = ReturnType<typeof useTranslations<"components.criterionPicker">>;

export function OccupationDivisionBlock({
  data,
  options,
  selected,
  onToggle,
}: OccupationDivisionBlockProps) {
  const t = useTranslations("components.criterionPicker");
  const headingId = useId();
  const headingRef = useRef<HTMLParagraphElement>(null);
  const focusHeadingAfterRender = useRef(false);
  const [chosenId, setChosenId] = useState<string | null>(null);

  // Focus management for the confirm step (WCAG 2.4.3): the button that was activated unmounts with
  // the view it belonged to, and the browser then drops focus to <body>. The heading is the group's
  // accessible name, so landing on it both restores the place and announces the outcome. A flag set
  // by the two handlers, not an effect keyed on `chosenId`: that effect would also run on mount and
  // pull focus out of the filter field the user is typing in.
  useEffect(() => {
    if (!focusHeadingAfterRender.current) return;
    focusHeadingAfterRender.current = false;
    headingRef.current?.focus();
  });

  const candidates = data.occupations;
  if (candidates.length === 0) return null;

  const choose = (id: string | null) => {
    focusHeadingAfterRender.current = true;
    setChosenId(id);
  };

  const only = candidates.length === 1 ? candidates[0] : undefined;
  const active: OccupationDivisionCandidate | null =
    only ?? candidates.find((c) => c.occupationGroupConceptId === chosenId) ?? null;

  if (active === null) {
    return (
      // A hairline and padding below: two interaction models ("pick one", then "tick several") must
      // not read as one list of rows, and the choice rows carry their own surface for the same reason.
      <div
        role="group"
        aria-labelledby={headingId}
        className="mb-1 flex flex-col gap-2 border-b border-border pb-3"
      >
        <p
          id={headingId}
          ref={headingRef}
          tabIndex={-1}
          className="text-body-sm font-medium text-text-primary"
        >
          {t("occupationChoose")}
        </p>
        <ul className="flex flex-col gap-1">
          {candidates.map((c) => (
            <li key={c.occupationGroupConceptId}>
              <button
                type="button"
                className="jp-occupation__choice flex w-full flex-wrap items-baseline gap-x-2.5 gap-y-0.5 rounded-md border border-border bg-surface-secondary px-3 py-2 text-start text-body-sm text-text-primary"
                onClick={() => choose(c.occupationGroupConceptId)}
              >
                <span className="min-w-0 break-words">{c.label}</span>
                <span className="text-(length:--text-caption) tabular-nums text-text-secondary max-sm:w-full sm:ms-auto">
                  {candidateStatus(t, c)}
                </span>
              </button>
            </li>
          ))}
        </ul>
      </div>
    );
  }

  const rows = active.state === "profiled" ? resolveRows(active.divisions ?? [], options) : [];

  return (
    <div role="group" aria-labelledby={headingId} className="flex flex-col gap-2">
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <p
          id={headingId}
          ref={headingRef}
          tabIndex={-1}
          className="text-body-sm font-medium text-text-primary"
        >
          {t("occupationHeading", { label: active.label })}
        </p>
        {candidates.length > 1 && (
          <button type="button" className="jp-textaction" onClick={() => choose(null)}>
            {t("occupationChangeChoice")}
          </button>
        )}
      </div>

      {active.state === "profiled" && (
        <>
          <p className="text-body-sm text-text-primary">
            {t("occupationIntro", { count: active.totalAds ?? 0 })}
          </p>
          {/* No box at all when no row survives (every division under the cut, or none the reference
              tree carries): the two lines below still answer the colon, and an empty bordered
              rectangle would be a container for nothing. */}
          {rows.length > 0 && (
            <div className="rounded-md border border-border">
              {rows.map(({ division, option }) => {
                const state = groupTriState(selected, option.leafCodes);
                const share = formatShare(t, division.sharePercent, division.adCount);
                return (
                  <div
                    key={division.code}
                    role="checkbox"
                    aria-checked={state === "indeterminate" ? "mixed" : state === "checked"}
                    // Name from author, parity the picker's filtered rows: code first as the level cue,
                    // then the name, then the share as an annotation with a comma for prosody.
                    aria-label={`${option.code} ${option.name}, ${share}`}
                    tabIndex={0}
                    onClick={() => onToggle(option.leafCodes)}
                    onKeyDown={(e) => {
                      if (e.key === " " || e.key === "Enter") {
                        e.preventDefault();
                        onToggle(option.leafCodes);
                      }
                    }}
                    className="group jp-criterionrow jp-criterionrow--filtered flex cursor-pointer flex-wrap items-center gap-x-2.5 gap-y-0.5 border-b border-border py-2 pe-3 ps-3 text-body-sm text-text-primary last:border-b-0 sm:flex-nowrap"
                  >
                    <CheckBox state={state} />
                    <span className="min-w-0 break-words max-sm:flex-1">{option.name}</span>
                    <span className="w-full min-w-0 text-(length:--text-caption) tabular-nums text-text-secondary max-sm:order-last max-sm:ps-8 sm:ms-auto sm:w-auto sm:shrink-0 sm:ps-2">
                      {share}
                    </span>
                    <span
                      className={cn(
                        "jp-mono shrink-0 text-(length:--text-caption) tabular-nums text-text-secondary ms-auto sm:ms-0 sm:ps-2",
                        state === "unchecked"
                          ? "invisible group-hover:visible group-focus-visible:visible"
                          : "visible",
                      )}
                    >
                      {option.code}
                    </span>
                  </div>
                );
              })}
            </div>
          )}
          {/* The rest of the denominator, in two plain lines that are never checkable: the real
              huvudgrupper under the share cut (only when there are any), and the employers with no
              huvudgrupp in the register — outside it, or in it without an SNI code. */}
          {(active.belowThresholdAdCount ?? 0) > 0 && (
            <p className="text-(length:--text-caption) text-text-secondary">
              {t("occupationBelowThreshold", {
                share: formatShare(
                  t,
                  active.belowThresholdSharePercent ?? 0,
                  active.belowThresholdAdCount ?? 0,
                ),
              })}
            </p>
          )}
          <p className="text-(length:--text-caption) text-text-secondary">
            {t("occupationWithoutDivision", {
              share: formatShare(
                t,
                active.withoutDivisionSharePercent ?? 0,
                active.withoutDivisionAdCount ?? 0,
              ),
            })}
          </p>
        </>
      )}

      {active.state === "tooFewAds" && (
        <p className="text-body-sm text-text-primary">
          {t("occupationTooFew", { count: active.totalAds ?? 0 })}
        </p>
      )}

      {active.state === "notProfiled" && (
        <p className="text-body-sm text-text-primary">{t("occupationNotProfiled")}</p>
      )}

      {/* The evidence the derivation rests on — the occupation name the word matched — in the same
          "träff på" form the alias rows use: a result with no visible reason is what §5 rules out.
          Last, so the heading and the intro read as the one paragraph they are. */}
      <p className="text-(length:--text-caption) text-text-secondary">
        {t("matchedVia", { term: active.matchedOn })}
      </p>
    </div>
  );
}

// A division the reference tree does not carry cannot be ticked, so it is not listed: the row's
// whole point is the toggle, and a code without a name is a claim without a home.
function resolveRows(
  divisions: ReadonlyArray<DivisionShare>,
  options: ReadonlyArray<CriterionOption>,
): Array<{ division: DivisionShare; option: CriterionOption }> {
  const out: Array<{ division: DivisionShare; option: CriterionOption }> = [];
  for (const division of divisions) {
    const option = options.find((o) => o.depth === 1 && o.code === division.code);
    if (option !== undefined) out.push({ division, option });
  }
  return out;
}

// A whole percent that rounds to zero beside a non-zero count would be a printed zero that is not
// one. The rows cannot reach it (the 5 % cut), the two plain lines can.
function formatShare(t: T, percent: number, count: number): string {
  return percent === 0 && count > 0
    ? t("occupationShareUnderOne", { count })
    : t("occupationShare", { percent, count });
}

function candidateStatus(t: T, c: OccupationDivisionCandidate): string {
  switch (c.state) {
    case "profiled":
      return t("occupationAdsSeen", { count: c.totalAds ?? 0 });
    case "tooFewAds":
      return t("occupationTooFewShort", { count: c.totalAds ?? 0 });
    case "notProfiled":
      return t("occupationNotProfiledShort");
    default:
      return "";
  }
}
