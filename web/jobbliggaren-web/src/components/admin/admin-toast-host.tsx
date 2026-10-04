"use client";

// Client: subscribes to the receipt store and runs the receipt's clock.
import { useEffect, useState, useSyncExternalStore } from "react";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";
import {
  dismissAdminToast,
  getAdminToastHeld,
  getAdminToastHeldServerSnapshot,
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
 * and the clock stops while the toast is hovered or focused, or while a dialog holds it (WCAG 2.2.1).
 * The live region is always mounted, empty when there is no toast, so a screen reader hears what is
 * put into it.
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
  const held = useSyncExternalStore(subscribeAdminToast, getAdminToastHeld, getAdminToastHeldServerSnapshot);
  const [hovered, setHovered] = useState(false);
  const [focused, setFocused] = useState(false);
  const paused = held || hovered || focused;
  const { token } = toast;

  // Each resume gives the reader a full 8 seconds.
  useEffect(() => {
    if (paused) return;
    const timer = setTimeout(() => dismissAdminToast(token), AUTO_CLOSE_MS);
    return () => clearTimeout(timer);
  }, [token, paused]);

  return (
    <div
      className="jp-toast"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={() => setFocused(false)}
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
