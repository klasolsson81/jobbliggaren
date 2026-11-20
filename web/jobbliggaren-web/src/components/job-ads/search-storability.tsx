"use client";

import { createContext, useContext, useEffect, useState } from "react";
import type { ReactNode } from "react";
import type { JobbUrlState } from "@/lib/job-ads/search-params";
import { sameUrlState } from "@/lib/job-ads/tokenize";

/**
 * #2199 PR 2b — whether the search on screen may be stored in Senaste sökningar, carried from the results
 * to the hero. The backend's capture gate decides it (`isStorableAsRecentSearch` on the list page), and the
 * hero offers "Spara sökningen" only when it says yes for the search the hero is showing. The frontend never
 * holds the rule: a personnummer or a religious occupation simply comes back `false`.
 *
 * The verdict is TAGGED with the search it answers rather than cleared on unmount, because the old results
 * stay mounted through the transition while the hero already shows the next search: an untagged `true`
 * would offer to save a search the backend will refuse (dotnet-architect and design-reviewer, 2b plan
 * reads). It is a pure function of the search, so a verdict is valid exactly when its tag matches; the
 * comparison is `sameUrlState`, the hero's own.
 *
 * Shape after `components/common/announcer.tsx`: the provider sits outside the results' Suspense boundary,
 * and the results push into it from an effect.
 */
interface Verdict {
  search: JobbUrlState;
  storable: boolean;
  committed: boolean;
}

const VerdictContext = createContext<Verdict | null>(null);
const ReportContext = createContext<((verdict: Verdict) => void) | null>(null);

export function SearchStorability({ children }: { children: ReactNode }) {
  const [verdict, setVerdict] = useState<Verdict | null>(null);
  return (
    <ReportContext.Provider value={setVerdict}>
      <VerdictContext.Provider value={verdict}>{children}</VerdictContext.Provider>
    </ReportContext.Provider>
  );
}

/**
 * Renders nothing; reports the backend's verdict for `search` while it is mounted. `committed` says the
 * list was fetched with the commit flag.
 */
export function ReportSearchStorability({
  search,
  storable,
  committed,
}: {
  search: JobbUrlState;
  storable: boolean;
  committed: boolean;
}) {
  const report = useContext(ReportContext);
  useEffect(() => {
    report?.({ search, storable, committed });
  }, [report, search, storable, committed]);
  return null;
}

/**
 * True only when the latest verdict answers `search` and says it may be stored. No verdict, a verdict for
 * another search, or no provider at all is `false`: the action is then simply not offered.
 */
export function useIsSearchStorable(search: JobbUrlState): boolean {
  const verdict = useContext(VerdictContext);
  return verdict !== null && verdict.storable && sameUrlState(verdict.search, search);
}

export function useIsSearchSaved(search: JobbUrlState): boolean {
  const verdict = useContext(VerdictContext);
  return (
    verdict !== null && verdict.committed && verdict.storable && sameUrlState(verdict.search, search)
  );
}
