"use client";

import { useEffect } from "react";
import { stripBounceMarker } from "@/lib/auth/strip-bounce-marker";

/**
 * Takes the cookie bounce's marker off the address bar once the public job page is up (ADR 0165), so the link
 * a visitor copies from there is the clean one. Renders nothing; without JavaScript the marker stays, which
 * does no harm, because the canonical link is clean.
 */
export function StripBounceMarker() {
  useEffect(() => {
    const clean = stripBounceMarker(window.location.href);
    if (clean !== null) window.history.replaceState(window.history.state, "", clean);
  }, []);
  return null;
}
