"use client";

// "use client": captures browser openers and restores modal focus.

import { useCallback, type RefObject } from "react";
import { useRouteModalFocus } from "@/lib/hooks/use-route-modal-focus";
import { destinationForPath, destinationHref } from "./destinations";
import { useInformationAdapter, useInformationSnapshot } from "./useInformationAdapter";
import type { InformationSnapshots } from "./types";

function modalOpenerHref(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const destination = destinationForPath(value);
  if (destination?.kind !== "job" && destination?.kind !== "application" &&
      destination?.kind !== "guestJob" && destination?.kind !== "guestApplication") return null;
  return destinationHref(destination);
}

function anchorDestinationHref(anchor: HTMLAnchorElement): string | null {
  const href = anchor.getAttribute("href");
  if (!href || href.includes("#")) return null;
  return modalOpenerHref(href.split("?")[0]);
}

function readModalOpener(value: unknown): InformationSnapshots["modalOpener"] {
  if (!value || typeof value !== "object" || !("kind" in value)) return null;
  if (value.kind === "main") return { kind: "main" };
  if (value.kind !== "detail" || !("href" in value) || !("hasAriaLabel" in value) ||
      typeof value.hasAriaLabel !== "boolean") return null;
  const href = modalOpenerHref(value.href);
  return href && href === value.href ? { kind: "detail", href, hasAriaLabel: value.hasAriaLabel } : null;
}

export function useInformationModalFocus(
  panelRef: RefObject<HTMLElement | null>,
  closeRef: RefObject<HTMLElement | null>,
) {
  const restored = readModalOpener(useInformationSnapshot("modalOpener"));
  const restoredKind = restored?.kind;
  const restoredHref = restored?.kind === "detail" ? restored.href : undefined;
  const restoredHasAriaLabel = restored?.kind === "detail" ? restored.hasAriaLabel : undefined;
  const resolveRestoredOpener = useCallback((panel: HTMLElement | null) => {
    if (restoredKind === "main") {
      const matches = [...document.querySelectorAll<HTMLElement>("main#main")].filter(main =>
        !panel?.contains(main));
      return matches.length === 1 ? matches[0] ?? null : null;
    }
    if (!restoredHref) return null;
    const matches = [...document.querySelectorAll<HTMLAnchorElement>("a[href]")].filter(link =>
      anchorDestinationHref(link) === restoredHref &&
      link.hasAttribute("aria-label") === restoredHasAriaLabel && !panel?.contains(link));
    return matches.length === 1 ? matches[0] ?? null : null;
  }, [restoredKind, restoredHref, restoredHasAriaLabel]);
  const openerRef = useRouteModalFocus(panelRef, closeRef, restored ? resolveRestoredOpener : undefined);
  useInformationAdapter("modalOpener", () => {
    if (restored) return restored;
    const opener = openerRef.current;
    if (opener?.tagName === "MAIN" && opener.id === "main") return { kind: "main" };
    if (!(opener instanceof HTMLAnchorElement)) return null;
    const href = anchorDestinationHref(opener);
    return href ? { kind: "detail", href, hasAriaLabel: opener.hasAttribute("aria-label") } : null;
  });
}
