"use client";

import { useCallback, useLayoutEffect, useRef } from "react";

export function useReturnFocus(open: boolean) {
  const openerRef = useRef<HTMLElement | null>(null);

  useLayoutEffect(() => {
    if (!open) return;
    const active = document.activeElement;
    openerRef.current = active instanceof HTMLElement ? active : null;
  }, [open]);

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
