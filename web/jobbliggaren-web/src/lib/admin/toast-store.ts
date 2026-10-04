/**
 * The admin surface's receipt toast: a module store, so an action in the account panel and the one
 * host in the layout share it without a provider (the `/ansokningar` toast's idiom). Publishing
 * replaces the current toast; `token` keeps a replaced toast's timer from dismissing its successor.
 */
export interface AdminToast {
  readonly token: number;
  readonly message: string;
}

let current: AdminToast | null = null;
let nextToken = 1;
const listeners = new Set<() => void>();

function emit(): void {
  for (const listener of listeners) listener();
}

/** Publishes a receipt and returns its token. Only a completed action publishes one (ADR 0150 D2). */
export function showAdminToast(message: string): number {
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
