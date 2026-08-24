import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import Loading from "./loading";

/**
 * #1092 — the CALL SITE of the live region on the route-level loading path.
 *
 * Same reason as `page.test.tsx`'s block: `Announce` is inert without a provider by design, so
 * removing `<Announcer>` from this file leaves no type error, no runtime error and every
 * other test green while a cross-route navigation to `/foretag/sok` announces nothing.
 * `code-reviewer` Major 3 on PR #1504.
 *
 * This region is its OWN node, not the one `page.tsx` mounts — a cross-route navigation swaps the
 * whole subtree, so the opening sentence lands here and the closing one lands there. Both are empty
 * when they mount, which is what ARIA22 asks of each; the criterion is about ordering within a
 * region, not about one region spanning a whole cycle.
 */
describe("/foretag/sok loading.tsx — the skeleton has a region to announce through", () => {
  it("puts the sentence in the region AND leaves it visible", () => {
    const { container } = render(<Loading />);

    const live = container.querySelector('p[role="status"][aria-live="polite"]');
    expect(live).not.toBeNull();
    expect(live).toHaveAttribute("aria-atomic", "true");
    // The region actually receives it on this path too — `useEffect` runs after the region is
    // committed, so the ordering ARIA22 requires holds even though both mount in one commit.
    expect(live).toHaveTextContent("Söker företag…");

    // Two elements carry the sentence by design: the region announces it, the visible line shows
    // it. `getAllByText` rather than `getByText` for exactly that reason — a sighted user must not
    // lose the text just because it is also announced.
    const visible = screen
      .getAllByText("Söker företag…")
      .filter((el) => !el.classList.contains("sr-only"));
    expect(visible).toHaveLength(1);
    // …and the visible one is ordinary content, not a second live region.
    expect(visible[0]).not.toHaveAttribute("role");
    expect(visible[0]).not.toHaveAttribute("aria-live");
  });

  it("keeps the skeleton itself free of any live-region role", () => {
    const { container } = render(<Loading />);

    // Exactly one region on this route: restoring `role="status"` on the skeleton would re-create
    // the unreliable shape and double the announcement.
    expect(container.querySelectorAll('[role="status"]')).toHaveLength(1);
    expect(container.querySelectorAll("[aria-live]")).toHaveLength(1);
    // `aria-busy` describes the skeleton subtree's state and is unrelated to the announcement.
    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
  });
});
