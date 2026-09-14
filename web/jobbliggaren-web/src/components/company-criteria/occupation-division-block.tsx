"use client";

import { useId, useState } from "react";
import { useTranslations } from "next-intl";
import { CheckBox } from "./criterion-tree";
import { cn } from "@/lib/utils";
import { groupTriState } from "@/lib/company-criteria/criterion-selection";
import type { CriterionOption } from "@/lib/company-criteria/criterion-options";
import type {
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
 * travels beside the share, it never collapses to a single division, and the not-in-register
 * bucket stays visible as a plain line so the distribution cannot read as complete.
 *
 * Several occupation groups for one word are a CHOICE the user makes first (AGENTS.md §5 — SSYK
 * derivation without user confirmation; ADR 0040 Beslut 4): the group names are listed, one is
 * picked, its map renders. One group skips the step. The choice is this component's own state, and
 * the picker mounts the block under `key={word}`, so a new word is a new block with no choice — the
 * reset is structural, not an effect. The floor and the share threshold are the server's; this
 * component renders the three states it is given and never a zero for "unknown".
 *
 * Not a live region, deliberately: the picker already rules "one region, one message" for its
 * filter outcome, and this block appears and disappears with the typed word like the rows do.
 */
export interface OccupationDivisionBlockProps {
  readonly data: OccupationDivisions;
  readonly options: ReadonlyArray<CriterionOption>;
  readonly selected: ReadonlySet<string>;
  readonly onToggle: (leafCodes: ReadonlyArray<string>) => void;
}

export function OccupationDivisionBlock({
  data,
  options,
  selected,
  onToggle,
}: OccupationDivisionBlockProps) {
  const t = useTranslations("components.criterionPicker");
  const headingId = useId();
  const [chosenId, setChosenId] = useState<string | null>(null);
  const candidates = data.occupations;
  if (candidates.length === 0) return null;

  const only = candidates.length === 1 ? candidates[0] : undefined;
  const active: OccupationDivisionCandidate | null =
    only ?? candidates.find((c) => c.occupationGroupConceptId === chosenId) ?? null;

  if (active === null) {
    return (
      <div role="group" aria-labelledby={headingId} className="flex flex-col gap-2">
        <p id={headingId} className="text-body-sm font-medium text-text-primary">
          {t("occupationChoose")}
        </p>
        <ul className="flex flex-col gap-1">
          {candidates.map((c) => (
            <li key={c.occupationGroupConceptId}>
              <button
                type="button"
                className="jp-occupation__choice flex w-full flex-wrap items-baseline gap-x-2.5 gap-y-0.5 rounded-md border border-border px-3 py-2 text-start text-body-sm text-text-primary"
                onClick={() => setChosenId(c.occupationGroupConceptId)}
              >
                <span className="min-w-0 break-words">{c.label}</span>
                <span className="text-(length:--text-caption) tabular-nums text-text-secondary sm:ms-auto">
                  {candidateStatus(t, c)}
                </span>
              </button>
            </li>
          ))}
        </ul>
      </div>
    );
  }

  return (
    <div role="group" aria-labelledby={headingId} className="flex flex-col gap-2">
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <p id={headingId} className="text-body-sm font-medium text-text-primary">
          {t("occupationHeading", { label: active.label })}
        </p>
        {candidates.length > 1 && (
          <button type="button" className="jp-clearlink" onClick={() => setChosenId(null)}>
            {t("occupationChangeChoice")}
          </button>
        )}
      </div>
      {/* The evidence the derivation rests on — the occupation name the word matched — in the same
          "träff på" form the alias rows use: a result with no visible reason is what §5 rules out. */}
      <p className="text-(length:--text-caption) text-text-secondary">
        {t("matchedVia", { term: active.matchedOn })}
      </p>

      {active.state === "profiled" && (
        <>
          <p className="text-body-sm text-text-primary">
            {t("occupationIntro", { count: active.totalAds ?? 0 })}
          </p>
          <div className="rounded-md border border-border">
            {(active.divisions ?? []).map((d) => {
              const option = options.find((o) => o.depth === 1 && o.code === d.code);
              // A division the reference tree does not carry cannot be ticked, so it is not listed:
              // the row's whole point is the toggle, and a code without a name is a claim without a home.
              if (option === undefined) return null;
              const state = groupTriState(selected, option.leafCodes);
              const share = t("occupationShare", { percent: d.sharePercent, count: d.adCount });
              return (
                <div
                  key={d.code}
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
                  <span className="w-full min-w-0 text-(length:--text-caption) tabular-nums text-text-secondary max-sm:order-last sm:ms-auto sm:w-auto sm:shrink-0 sm:ps-2">
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
          <p className="text-(length:--text-caption) text-text-secondary">
            {t("occupationNotInRegister", {
              percent: active.notInRegisterSharePercent ?? 0,
              count: active.notInRegisterAdCount ?? 0,
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
    </div>
  );
}

function candidateStatus(
  t: ReturnType<typeof useTranslations<"components.criterionPicker">>,
  c: OccupationDivisionCandidate,
): string {
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
