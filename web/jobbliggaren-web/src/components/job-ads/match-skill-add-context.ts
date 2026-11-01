"use client";

import { createContext, useContext } from "react";

/**
 * #2247 — what a missing-skill chip asks of the region around the skills box: start an add, and
 * whether one is already running (and which chip started it). The region owns the server action,
 * the refresh and the status line, because the chip that was clicked is gone once the refreshed
 * match moves the skill to "Matchar din profil".
 */
export interface MatchSkillAdd {
  pendingKey: string | null;
  add: (key: string, conceptIds: ReadonlyArray<string>, label: string) => void;
}

export const MatchSkillAddContext = createContext<MatchSkillAdd | null>(null);

export function useMatchSkillAdd(): MatchSkillAdd | null {
  return useContext(MatchSkillAddContext);
}
