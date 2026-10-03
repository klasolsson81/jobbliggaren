import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { useFocusMainOnUnmount } from "./use-focus-main-on-unmount";

// The surface a boundary renders: an <h1> that takes focus on mount (what
// useFocusOnMount does in every boundary) and the hook under test. Unmounting
// it is what a successful retry does — the actor is Next's ErrorBoundaryHandler,
// which swaps the surface for the refreshed segment.
function Surface() {
  useFocusMainOnUnmount();
  return (
    <h1 tabIndex={-1} ref={(el) => el?.focus()}>
      Sidan kunde inte visas
    </h1>
  );
}

function Page({ withSurface }: { withSurface: boolean }) {
  return (
    <>
      <main id="main" tabIndex={-1}>
        {withSurface ? <Surface /> : <p>recovered</p>}
      </main>
      <input aria-label="elsewhere" />
    </>
  );
}

describe("useFocusMainOnUnmount (#1949, design-reviewer Major 4)", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["requestAnimationFrame"] });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("FF1: the surface leaves with focus on <body>, so focus moves to #main on the next frame", () => {
    const { rerender } = render(<Page withSurface={true} />);
    expect(document.activeElement?.tagName).toBe("H1");

    rerender(<Page withSurface={false} />);
    // The focused <h1> is gone: the browser has dropped focus to <body>.
    expect(document.activeElement).toBe(document.body);

    vi.advanceTimersToNextFrame();
    expect(document.activeElement).toBe(document.getElementById("main"));
  });

  it("FF2: focus that is somewhere else when the surface leaves is left alone", () => {
    const { rerender, getByLabelText } = render(<Page withSurface={true} />);
    const elsewhere = getByLabelText("elsewhere");
    elsewhere.focus();
    expect(document.activeElement).toBe(elsewhere);

    rerender(<Page withSurface={false} />);
    vi.advanceTimersToNextFrame();
    expect(document.activeElement).toBe(elsewhere);
  });
});
