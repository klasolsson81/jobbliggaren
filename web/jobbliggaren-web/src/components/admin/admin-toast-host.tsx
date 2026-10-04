"use client";

import { useEffect, useRef, useSyncExternalStore } from "react";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";
import {
  dismissAdminToast,
  getAdminToastServerSnapshot,
  getAdminToastSnapshot,
  subscribeAdminToast,
  type AdminToast,
} from "@/lib/admin/toast-store";

const AUTO_CLOSE_MS = 8_000;
const HOST_CLASS = "jp-admintoasthost";

/** True for a pointer target inside the toast, which an open dialog must not read as outside. */
export function isInAdminToast(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest(`.${HOST_CLASS}`) !== null;
}

/**
 * The one renderer of the admin receipt toast, on the house `.jp-toast`. It closes after 8 seconds,
 * and the clock stops while the toast is hovered or focused (WCAG 2.2.1). The live region is always
 * mounted, empty when there is no toast, so a screen reader hears what is put into it.
 */
export function AdminToastHost() {
  const toast = useSyncExternalStore(
    subscribeAdminToast,
    getAdminToastSnapshot,
    getAdminToastServerSnapshot,
  );

  return (
    <div className={HOST_CLASS} aria-live="polite" role="status">
      {toast === null ? null : <ToastCard key={toast.token} toast={toast} />}
    </div>
  );
}

function ToastCard({ toast }: { readonly toast: AdminToast }) {
  const t = useTranslations("admin.users.toast");
  const pausedRef = useRef(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const { token } = toast;

  useEffect(() => {
    timerRef.current = setTimeout(() => {
      if (!pausedRef.current) dismissAdminToast(token);
    }, AUTO_CLOSE_MS);
    return () => {
      if (timerRef.current !== null) clearTimeout(timerRef.current);
    };
  }, [token]);

  function pause() {
    pausedRef.current = true;
    if (timerRef.current !== null) clearTimeout(timerRef.current);
  }

  function resume() {
    pausedRef.current = false;
    if (timerRef.current !== null) clearTimeout(timerRef.current);
    timerRef.current = setTimeout(() => dismissAdminToast(token), AUTO_CLOSE_MS);
  }

  return (
    <div
      className="jp-toast"
      onMouseEnter={pause}
      onMouseLeave={resume}
      onFocus={pause}
      onBlur={resume}
    >
      <span className="jp-toast__msg">{toast.message}</span>
      <button
        type="button"
        className="jp-toast__close"
        aria-label={t("close")}
        onClick={() => dismissAdminToast(token)}
      >
        <X size={16} aria-hidden="true" />
      </button>
    </div>
  );
}
