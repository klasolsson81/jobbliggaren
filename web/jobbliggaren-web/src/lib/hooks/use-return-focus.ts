"use client";

import { useCallback, useLayoutEffect, useRef } from "react";

/**
 * `returnTo` replaces the element focused when the dialog opened, for an opener that is gone by
 * the time the dialog closes: a menu item unmounts with its menu (#1827).
 */
export function useReturnFocus(open: boolean, returnTo: HTMLElement | null = null) {
  const openerRef = useRef<HTMLElement | null>(null);

  useLayoutEffect(() => {
    if (!open) return;
    const active = document.activeElement;
    openerRef.current = returnTo ?? (active instanceof HTMLElement ? active : null);
  }, [open, returnTo]);

  const returnFocus = useCallback(() => {
    const opener = openerRef.current;
    if (opener?.isConnected) opener.focus();
  }, []);

  const onCloseAutoFocus = useCallback(
    (event: Event) => {
      event.preventDefault();
      returnFocus();
    },
    [returnFocus],
  );

  return { onCloseAutoFocus, returnFocus };
}
