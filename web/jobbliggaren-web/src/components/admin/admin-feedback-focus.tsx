"use client";

// "use client": links that name where focus goes once the page they open has rendered, the island that
// moves it there, and a pager link that stays in place, inert, at its bound. Each needs the browser.
import Link from "next/link";
import { useLayoutEffect, type ComponentProps, type MouseEvent } from "react";

/**
 * Where focus goes after a navigation: the list's first submission, the page filter's line, one
 * submission's row (its id in lower case), or the control that was pressed, kept in view.
 */
export type FeedbackFocusTarget = "list" | "scope" | "keep" | `item:${string}`;

/** What the last press named, and the URL it named it for: taken once that URL has rendered. */
let pending: { readonly href: string; readonly target: FeedbackFocusTarget } | null = null;

type LinkProps = ComponentProps<typeof Link>;

/** A press next/link leaves to the browser, such as a new tab, which renders nothing here. */
function leftToBrowser(event: MouseEvent<HTMLAnchorElement>): boolean {
  return event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey;
}

/**
 * A link whose own line is gone from the page it opens, so focus would fall to the page's start: it names
 * a receiver on that page instead (DESIGN.md §9, WCAG 2.4.3). The page it opens keeps the scroll where it
 * is, and the receiver's focus brings it into view; with no receiver on screen, the pressed link keeps
 * focus where it stands. A link to the page already shown names nothing.
 */
export function FeedbackFocusLink({
  focusTo,
  onClick,
  scroll = false,
  ...props
}: LinkProps & { readonly focusTo: FeedbackFocusTarget }) {
  return (
    <Link
      {...props}
      scroll={scroll}
      onClick={(event) => {
        onClick?.(event);
        const { href } = event.currentTarget;
        if (event.defaultPrevented || leftToBrowser(event) || href === window.location.href) return;
        pending = { href, target: focusTo };
      }}
    />
  );
}

/** Moves focus to the receiver the last FeedbackFocusLink named, once the page at `location` has rendered. */
export function FeedbackFocusReceiver({ location }: { readonly location: string }) {
  // A layout effect, so focus moves before the browser paints the page without the link that held it.
  useLayoutEffect(() => {
    if (pending === null || new URL(location, window.location.origin).href !== pending.href) return;
    const { target } = pending;
    pending = null;
    if (target === "keep") {
      const active = document.activeElement;
      if (active instanceof HTMLElement && active !== document.body) active.scrollIntoView?.({ block: "nearest" });
      else receiverOf("list")?.focus();
      return;
    }
    receiverOf(target)?.focus();
  }, [location]);
  return null;
}

/**
 * The receiver, or the next one that is on screen: below 1100 px an open submission hides the list and
 * the page filter's line, and a hidden element cannot take focus. With none, focus stays where it is.
 */
function receiverOf(target: Exclude<FeedbackFocusTarget, "keep">): HTMLElement | null {
  const shown = (selector: string) => {
    const element = document.querySelector<HTMLElement>(selector);
    return element !== null && (element.checkVisibility?.() ?? true) ? element : null;
  };
  const list = () => shown(".jp-adminfeedback__list a") ?? shown('[data-feedback-focus="list"]');
  if (target === "scope") return shown('[data-feedback-focus="scope"]') ?? list();
  if (target === "list") return list();
  return shown(`[data-feedback-item="${target.slice("item:".length)}"]`) ?? list();
}

/**
 * Föregående or Nästa, on every page and in the same place (the account pager's form). At the first or the
 * last page it stays, `aria-disabled` and inert, so the control a keyboard pressed keeps focus; the page it
 * opens keeps the scroll, and the control is brought into view where the list's length moved it.
 */
export function FeedbackPagerLink({
  disabled,
  rel,
  ...props
}: Omit<LinkProps, "onClick" | "scroll" | "prefetch"> & { readonly disabled: boolean }) {
  return (
    <FeedbackFocusLink
      {...props}
      focusTo="keep"
      rel={disabled ? undefined : rel}
      prefetch={disabled ? false : undefined}
      aria-disabled={disabled || undefined}
      onClick={(event) => {
        if (disabled) event.preventDefault();
      }}
    />
  );
}
