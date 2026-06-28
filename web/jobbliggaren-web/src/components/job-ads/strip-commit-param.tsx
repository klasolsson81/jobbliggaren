"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { COMMIT_PARAM } from "@/lib/job-ads/search-params";

interface StripCommitParamProps {
  /** Server-känt: searchParams bär ?commit=1 (E2j commit-intent). */
  active: boolean;
}

/**
 * Fas E2j (ADR 0060 amendment 2026-06-12) — strippar `?commit=1` ur URL:en
 * efter mount. Commit-flaggan är en transient signal som gatar backend-
 * auto-capture; den får inte ligga kvar i adressfältet (en delad/bokmärkt
 * `?...&commit=1`-länk skulle annars re-capturera sökningen hos mottagaren,
 * och flaggan förorenar URL-renheten).
 *
 * Render-null client-ö. `router.replace` till samma URL
 * utan flaggan — eftersom `commit` ALDRIG ingår i `JobbUrlState`/`sameUrlState`
 * ser hero-spegelfältets own-roundtrip/skip-guard-mekanik den som en ren
 * icke-state-ändring och serialiserar INTE om användarens text (E2i-invariant).
 * Strip-replacen bär aldrig själv `commit=1`.
 */
export function StripCommitParam({ active }: StripCommitParamProps) {
  const router = useRouter();

  useEffect(() => {
    if (!active) return;
    // window.location läses först i effekten (client-only) — ren URL byggs ur
    // det faktiska adressfältet så övriga params bevaras exakt.
    const url = new URL(window.location.href);
    if (!url.searchParams.has(COMMIT_PARAM)) return;
    url.searchParams.delete(COMMIT_PARAM);
    router.replace(`${url.pathname}${url.search}`, { scroll: false });
  }, [active, router]);

  return null;
}
