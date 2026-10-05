import type { ReactNode } from "react";

/**
 * The admin surface's receipt toast: a module store, so an action in the account panel and the one
 * host in the layout share it without a provider (the `/ansokningar` toast's idiom). Publishing
 * replaces the current toast; `token` keeps a replaced toast's timer from dismissing its successor.
 */
export interface AdminToast {
  readonly token: number;
  readonly message: ReactNode;
}

let current: AdminToast | null = null;
let nextToken = 1;
let holds = 0;
const listeners = new Set<() => void>();

function emit(): void {
  for (const listener of listeners) listener();
}

/** Publishes a receipt and returns its token. Only a completed action publishes one (ADR 0150 D2). */
export function showAdminToast(message: ReactNode): number {
  const token = nextToken++;
  current = { token, message };
  emit();
  return token;
}

/** Dismisses the toast only while it is still the one the token names. */
export function dismissAdminToast(token: number): void {
  if (current?.token !== token) return;
  current = null;
  emit();
}

/**
 * Stops the receipt's clock while a dialog keeps keyboard focus away from it; the returned
 * function releases the hold, once.
 */
export function holdAdminToasts(): () => void {
  holds += 1;
  emit();
  let released = false;
  return () => {
    if (released) return;
    released = true;
    holds -= 1;
    emit();
  };
}

export function subscribeAdminToast(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function getAdminToastSnapshot(): AdminToast | null {
  return current;
}

export function getAdminToastServerSnapshot(): AdminToast | null {
  return null;
}

export function getAdminToastHeld(): boolean {
  return holds > 0;
}

export function getAdminToastHeldServerSnapshot(): boolean {
  return false;
}
