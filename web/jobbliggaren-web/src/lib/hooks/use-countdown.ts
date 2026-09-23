"use client";

// "use client": a countdown hook holding timer state — browser-only.

import { useEffect, useState } from "react";

/**
 * Whole seconds left of a cooldown, counted down once a second to zero.
 *
 * `restartKey` names the thing being counted (a challenge's send time): a new key starts the count
 * again from `initialSeconds`. The reset happens during render, not in an effect, so the new count is
 * on screen in the same paint as whatever brought the new key. Between key changes `initialSeconds`
 * is read only once, which is what lets a caller hand in the seconds left of a count that began before
 * it mounted.
 */
export function useCountdown(initialSeconds: number, restartKey: number): number {
  const [seconds, setSeconds] = useState(initialSeconds);
  const [countedFor, setCountedFor] = useState(restartKey);
  if (countedFor !== restartKey) {
    setCountedFor(restartKey);
    setSeconds(initialSeconds);
  }

  useEffect(() => {
    if (seconds <= 0) return;
    const id = setInterval(() => {
      setSeconds((left) => (left <= 1 ? 0 : left - 1));
    }, 1000);
    return () => clearInterval(id);
  }, [seconds]);

  return seconds;
}
