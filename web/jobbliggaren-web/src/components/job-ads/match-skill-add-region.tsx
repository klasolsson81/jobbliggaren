"use client";

import { useCallback, useEffect, useMemo, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { addPreferredSkillsAction } from "@/lib/actions/match-preferences";
import { MatchSkillAddContext, type MatchSkillAdd } from "./match-skill-add-context";
import styles from "./job-ad-match-section.module.css";

/**
 * #2247 — wraps the server-rendered skill groups so a missing-skill chip can be added to the profile
 * in one click. Nothing moves on the client: after the backend has stored the skill the route is
 * refreshed, the match is computed again, and the chip reappears under "Matchar din profil" only if
 * the backend now says it matches.
 *
 * The clicked button does not survive that refresh, so the outcome is said here, in a status line
 * that is in the DOM from the start (a live region added late is not announced). Focus moves to it
 * once the refreshed groups are on screen; a failure leaves focus on the chip and is an alert.
 */
export function MatchSkillAddRegion({ children }: { children: React.ReactNode }) {
  const t = useTranslations("jobads.ui.match.skills.add");
  const router = useRouter();
  const [isPending, startTransition] = useTransition();
  const [pendingKey, setPendingKey] = useState<string | null>(null);
  const [added, setAdded] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const statusRef = useRef<HTMLParagraphElement>(null);
  const focusStatus = useRef(false);

  useEffect(() => {
    if (!isPending && focusStatus.current) {
      focusStatus.current = false;
      statusRef.current?.focus();
    }
  }, [isPending]);

  const add = useCallback(
    (key: string, conceptIds: ReadonlyArray<string>, label: string) => {
      if (isPending) return;
      setPendingKey(key);
      setAdded(null);
      setError(null);
      startTransition(async () => {
        const result = await addPreferredSkillsAction(conceptIds);
        if (result.success) {
          setAdded(t("added", { label }));
          focusStatus.current = true;
          router.refresh();
        } else {
          setError(result.error);
        }
        setPendingKey(null);
      });
    },
    [isPending, router, t],
  );

  const value = useMemo<MatchSkillAdd>(
    () => ({ pendingKey: isPending ? pendingKey : null, add }),
    [add, isPending, pendingKey],
  );

  return (
    <MatchSkillAddContext.Provider value={value}>
      {children}
      <p ref={statusRef} role="status" tabIndex={-1} className={styles.addStatus}>
        {added}
      </p>
      {error !== null && (
        <p role="alert" className={styles.addError}>
          {error}
        </p>
      )}
    </MatchSkillAddContext.Provider>
  );
}
