"use client";

import { createContext, useContext, useEffect, useLayoutEffect, useMemo, useRef, useSyncExternalStore } from "react";
import { usePathname, useRouter } from "next/navigation";
import { destinationForPath, destinationHref, isInformationPath, validInformationHref, type ReturnDestination } from "./destinations";
import type { InformationSnapshots, SnapshotKey } from "./types";

const MARKER = "jobbliggarenInformation";
type Marker = { id: string; step: 0 | 1 };
type Excursion = {
  id: string; sourcePath: string; destination: ReturnDestination; informationPath: string;
  focusId: string; windowScroll: { left: number; top: number };
  scroll: { key: string; left: number; top: number }[];
  snapshots: Map<SnapshotKey, unknown>; pending: boolean; returnRequested: boolean;
};
type Navigation = {
  subscribe: (listener: () => void) => () => void;
  version: () => number;
  destination: () => ReturnDestination | null;
  navigate: (href: string, trigger: HTMLAnchorElement) => boolean;
  back: () => boolean;
  register: <K extends SnapshotKey>(key: K, capture: () => InformationSnapshots[K]) => () => void;
  read: <K extends SnapshotKey>(key: K) => InformationSnapshots[K] | undefined;
};
export const InformationNavigationContext = createContext<Navigation | null>(null);

function marker(): Marker | null {
  const state: unknown = window.history.state;
  if (!state || typeof state !== "object" || !(MARKER in state)) return null;
  const own: unknown = (state as Record<string, unknown>)[MARKER];
  if (!own || typeof own !== "object") return null;
  const value = own as Record<string, unknown>;
  return typeof value.id === "string" && (value.step === 0 || value.step === 1)
    ? { id: value.id, step: value.step } : null;
}
function mark(value: Marker) {
  // The router's history state is preserved opaquely; no private Next fields are inspected.
  const state: unknown = window.history.state;
  window.history.replaceState({ ...(state && typeof state === "object" ? state : {}), [MARKER]: value }, "");
}
export function replaceInformationFragment(href: string): boolean {
  if (!isInformationPath(window.location.pathname) || !/^#[a-z][a-z0-9-]*$/.test(href)) return false;
  const target = document.getElementById(href.slice(1));
  if (!target) return false;
  window.history.replaceState(window.history.state, "", href);
  target.focus({ preventScroll: true });
  target.scrollIntoView({ block: "start" });
  return true;
}

// The root boundary keeps the excursion in memory while route-owned UI unmounts.
export function InformationReturnProvider({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const excursion = useRef<Excursion | null>(null);
  const captures = useRef(new Map<SnapshotKey, () => unknown>());
  const listeners = useRef(new Set<() => void>());
  const revision = useRef(0);
  const lastStep = useRef<0 | 1 | null>(null);

  useEffect(() => () => { excursion.current = null; captures.current.clear(); }, []);

  const navigation = useMemo<Navigation>(() => {
    const notify = () => { revision.current++; listeners.current.forEach(listener => listener()); };
    const active = (step: 0 | 1) => {
      const record = excursion.current;
      if (!record || typeof window === "undefined") return null;
      const own = marker();
      return own?.id === record.id && own.step === step &&
        window.location.pathname === (step === 0 ? record.sourcePath : record.informationPath)
        ? record : null;
    };
    const back = () => {
      const pending = excursion.current;
      if (pending?.pending) {
        pending.returnRequested = true;
        return true;
      }
      if (!active(1)) {
        if (!excursion.current) return false;
        excursion.current = null;
        lastStep.current = null;
        notify();
        router.push("/");
        return true;
      }
      router.back();
      return true;
    };
    return {
      subscribe(listener) { listeners.current.add(listener); return () => { listeners.current.delete(listener); }; },
      version: () => revision.current,
      destination: () => active(1)?.destination ?? null,
      back,
      navigate(href, trigger) {
        const pending = excursion.current;
        if (pending?.pending && window.location.pathname === pending.sourcePath && validInformationHref(href)) return true;
        const record = active(1);
        if (record && href === destinationHref(record.destination)) return back();
        if (!validInformationHref(href)) {
          excursion.current = null;
          lastStep.current = null;
          notify();
          return false;
        }
        const path = href.split("#")[0] ?? "";
        if (record) {
          if (path === record.informationPath) {
            const fragment = href.split("#")[1];
            if (fragment) {
              if (!replaceInformationFragment(`#${fragment}`)) window.history.replaceState(window.history.state, "", href);
            } else {
              window.history.replaceState(window.history.state, "", href);
              document.querySelector<HTMLElement>("#main h1[tabindex]")?.focus({ preventScroll: true });
              window.scrollTo({ left: 0, top: 0, behavior: "instant" });
            }
            return true;
          }
          record.informationPath = path;
          record.pending = true;
          router.replace(href);
          return true;
        }
        const sourcePath = window.location.pathname;
        const destination = destinationForPath(sourcePath);
        if (!destination || sourcePath === path || (isInformationPath(sourcePath) && sourcePath !== "/hjalpcenter")) {
          excursion.current = null;
          notify();
          if (isInformationPath(sourcePath)) { router.replace(href); return true; }
          return false;
        }
        const id = crypto.randomUUID();
        excursion.current = {
          id, sourcePath, destination, informationPath: path, focusId: trigger.id,
          windowScroll: { left: window.scrollX, top: window.scrollY },
          scroll: [...document.querySelectorAll<HTMLElement>("[data-information-scroll]")].map(el => ({ key: el.dataset.informationScroll ?? "", left: el.scrollLeft, top: el.scrollTop })),
          snapshots: new Map([...captures.current].map(([key, capture]) => [key, capture()])), pending: true, returnRequested: false,
        };
        mark({ id, step: 0 });
        lastStep.current = 0;
        router.push(href);
        return true;
      },
      register(key, capture) {
        captures.current.set(key, capture);
        const restored = active(0);
        if (restored && !restored.pending) restored.snapshots.delete(key);
        return () => { if (captures.current.get(key) === capture) captures.current.delete(key); };
      },
      read(key) { return active(0)?.snapshots.get(key) as InformationSnapshots[typeof key] | undefined; },
    };
  }, [router]);

  useLayoutEffect(() => {
    const record = excursion.current;
    if (!record) return;
    if (pathname === record.informationPath && record.pending) {
      mark({ id: record.id, step: 1 });
      record.pending = false;
    }
    const own = marker();
    const currentStep = own?.id === record.id ? own.step : null;
    const returning = currentStep === 0 && lastStep.current === 1 && pathname === record.sourcePath;
    if ((currentStep === 0 && pathname !== record.sourcePath) ||
        (currentStep === 1 && pathname !== record.informationPath) || currentStep === null) excursion.current = null;
    lastStep.current = currentStep;
    revision.current++;
    listeners.current.forEach(listener => listener());
    if (currentStep === 1 && !record.pending && record.returnRequested) {
      record.returnRequested = false;
      requestAnimationFrame(() => { if (excursion.current === record) navigation.back(); });
    }
    if (!returning) return;
    let cancelled = false;
    let frame = 0;
    const restore = () => {
      if (cancelled || excursion.current !== record || marker()?.step !== 0) return;
      const focusTarget = record.focusId ? document.getElementById(record.focusId) : null;
      // Source UI may stream in after the route commit; its state and mount effects go first.
      if (!focusTarget && frame++ < 60) { requestAnimationFrame(restore); return; }
      for (const position of record.scroll) {
        const el = [...document.querySelectorAll<HTMLElement>("[data-information-scroll]")].find(el => el.dataset.informationScroll === position.key);
        el?.scrollTo({ left: position.left, top: position.top, behavior: "instant" });
      }
      window.scrollTo({ ...record.windowScroll, behavior: "instant" });
      const fallback = document.querySelector<HTMLElement>('[role="dialog"] h2[tabindex]')
        ?? document.querySelector<HTMLElement>('main h1[tabindex], #main h1[tabindex]')
        ?? document.querySelector<HTMLElement>('main[tabindex]');
      (focusTarget ?? fallback)?.focus({ preventScroll: true });
    };
    requestAnimationFrame(() => requestAnimationFrame(restore));
    return () => { cancelled = true; };
  }, [pathname, navigation]);

  useEffect(() => {
    const originalReplace = window.history.replaceState.bind(window.history);
    let mounted = true;
    const replaceState = (data: unknown, unused: string, url?: string | URL | null) => {
      const record = mounted ? excursion.current : null;
      const own = marker();
      const currentPath = own?.step === 0 ? record?.sourcePath : record?.informationPath;
      const target = url == null ? new URL(window.location.href) : new URL(url, window.location.href);
      // Same-entry refreshes can replace state. Retain only proof verified before that call.
      const preserve = record && own?.id === record.id && window.location.pathname === currentPath &&
        target.origin === window.location.origin && target.pathname === currentPath &&
        data !== null && typeof data === "object" && !Array.isArray(data) && !(MARKER in data);
      originalReplace(preserve ? { ...data, [MARKER]: own } : data, unused, url);
    };
    window.history.replaceState = replaceState;
    const clear = () => { excursion.current = null; lastStep.current = null; revision.current++; listeners.current.forEach(listener => listener()); };
    const click = (event: MouseEvent) => {
      if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
      const anchor = event.target instanceof Element ? event.target.closest("a") : null;
      if (!anchor || anchor.target === "_blank" || anchor.hasAttribute("download")) return;
      const href = anchor.getAttribute("href") ?? "";
      if (anchor.hasAttribute("data-information-return") && navigation.back()) { event.preventDefault(); return; }
      if (href.startsWith("#") && replaceInformationFragment(href)) { event.preventDefault(); return; }
      if (anchor.hasAttribute("data-information-link") && navigation.navigate(href, anchor)) { event.preventDefault(); return; }
      if (!anchor.hasAttribute("data-information-link") && !href.startsWith("#")) clear();
    };
    const forward = () => {
      const record = excursion.current;
      const own = marker();
      if (!record || lastStep.current !== 0 || own?.id !== record.id || own.step !== 1) return;
      record.snapshots = new Map([...captures.current].map(([key, capture]) => [key, capture()]));
      record.windowScroll = { left: window.scrollX, top: window.scrollY };
      record.scroll = [...document.querySelectorAll<HTMLElement>("[data-information-scroll]")].map(el => ({ key: el.dataset.informationScroll ?? "", left: el.scrollLeft, top: el.scrollTop }));
    };
    document.addEventListener("click", click, true);
    document.addEventListener("submit", clear, true);
    window.addEventListener("pagehide", clear);
    window.addEventListener("popstate", forward);
    return () => {
      mounted = false;
      if (window.history.replaceState === replaceState) window.history.replaceState = originalReplace;
      document.removeEventListener("click", click, true); document.removeEventListener("submit", clear, true); window.removeEventListener("pagehide", clear); window.removeEventListener("popstate", forward);
    };
  }, [navigation]);
  return <InformationNavigationContext.Provider value={navigation}>{children}</InformationNavigationContext.Provider>;
}

export function useInformationNavigation() {
  const navigation = useContext(InformationNavigationContext);
  useSyncExternalStore(navigation?.subscribe ?? (() => () => {}), navigation?.version ?? (() => 0), () => 0);
  return navigation;
}
