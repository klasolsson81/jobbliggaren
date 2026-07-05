"use client";

/**
 * Ephemeral, client-only anchor for the /ansokningar detail drawer (ADR 0092 D7,
 * Approach A). A list row / attention card click records the pointer's viewport Y
 * (for near-click vertical positioning, design handoff §9) and the triggering
 * element (for WCAG 2.4.3 focus-return on close). The drawer shell reads it once
 * on mount and resets it on unmount.
 *
 * It is deliberately NOT a §5 "stateful static helper": it is browser-only
 * presentation state (like a scroll position), never read on the server (no SSR
 * request-sharing), and reset after each open. A `reset` export keeps it testable.
 * The soft-nav (row Link -> intercepting route) keeps the same JS runtime, so a
 * value written in the row's onClick survives until the shell mounts.
 */
export interface DrawerAnchor {
  clientY: number;
  trigger: HTMLElement | null;
}

let anchor: DrawerAnchor | null = null;

export function setDrawerAnchor(
  clientY: number,
  trigger: HTMLElement | null,
): void {
  anchor = { clientY, trigger };
}

/** Peek the current anchor (the shell reads it once on mount). */
export function readDrawerAnchor(): DrawerAnchor | null {
  return anchor;
}

export function resetDrawerAnchor(): void {
  anchor = null;
}
