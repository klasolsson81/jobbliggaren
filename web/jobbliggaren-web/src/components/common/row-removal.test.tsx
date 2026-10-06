import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { RemovalStatus } from "@/components/common/row-removal";

// The focus half of useRowRemoval is pinned through its two owners' lists, where the commits that
// drop a row are real (saved-job-ad-list.test.tsx, recent-search-list.test.tsx).
const region = (c: HTMLElement) => c.querySelector('p[role="status"]');

describe("RemovalStatus — the receipt's own region (#2029)", () => {
  it("is in the DOM, empty, polite and atomic before any receipt, with no name of its own", () => {
    const { container } = render(<RemovalStatus receipt={{ seq: 0, text: "" }} />);

    const live = region(container);
    expect(live).not.toBeNull();
    expect(live).toHaveAttribute("aria-live", "polite");
    expect(live).toHaveAttribute("aria-atomic", "true");
    expect(live).toHaveTextContent("");
    expect(live?.childElementCount).toBe(0);
    // `role=status` is name-from-author; a label would give the region a name of its own.
    expect(live).not.toHaveAttribute("aria-label");
    expect(live).not.toHaveAttribute("aria-labelledby");
  });

  it("puts the same sentence twice into a new node each time, in the same region", () => {
    const sentence = "Bokmärket har tagits bort.";
    const { container, rerender } = render(<RemovalStatus receipt={{ seq: 1, text: sentence }} />);
    const live = region(container);
    const first = live?.firstElementChild;

    rerender(<RemovalStatus receipt={{ seq: 2, text: sentence }} />);

    expect(region(container)).toBe(live);
    expect(live).toHaveTextContent(sentence);
    expect(live?.firstElementChild).not.toBeNull();
    expect(live?.firstElementChild).not.toBe(first);
  });
});
