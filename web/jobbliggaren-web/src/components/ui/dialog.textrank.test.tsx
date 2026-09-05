import { describe, it, expect } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

import { InfoDialog } from "@/components/common/info-dialog";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "./dialog";

/**
 * #1601 regression guard — CONTRACT level, and the limit is the point.
 *
 * jsdom has no cascade, so this file cannot read a rendered `font-size`; it pins the
 * class contract that decides one. The rendered half is owed separately and was taken
 * in a real browser for this change (computed style at 1280, every consumer surface) —
 * a surviving class name is not a rendered style, and only the browser can say whether
 * Tailwind emitted a rule for it at all. Same division as `dialog.zindex.test.tsx`.
 *
 * The defect: `cn` is bare `twMerge`, which does not know this project's `--text-*`
 * namespace, so an authored size class (`text-body-sm`, `text-h4`) is classified as a
 * COLOUR and collides with the colour class beside it — inside the primitive's own
 * default string, before any caller is involved. The size lost every time. `InfoDialog`
 * is only where it became visible: it renders paragraph 1 through `DialogDescription`
 * and the rest through a plain `<div>` that never passes through `cn`, so two paragraphs
 * of the same rank rendered at two sizes.
 *
 * The primitive therefore expresses its size in the font-size group instead, which does
 * not collide with a colour. A caller may still override the size deliberately — that is
 * the API — but can no longer erase it by naming a colour.
 *
 * The root cause is `cn` itself, and it silently drops authored sizes in other primitives
 * too; that surface is measured and carried by its own issue, not by this file.
 */

/** The size-bearing class the primitive applies, however it is spelled. */
const SIZE_CLASS = /text-\(length:--text-[\w-]+\)|text-\[length:var\(--text-[\w-]+\)\]/;

function classOf(selector: string): string {
  return document.querySelector(selector)?.getAttribute("class") ?? "";
}

describe("Dialog body-text rank (#1601)", () => {
  it("keeps the description's size when a caller names a colour", () => {
    render(
      <Dialog open>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Titel</DialogTitle>
            {/* Exactly what InfoDialog passed, and what made the collision visible. */}
            <DialogDescription className="text-text-primary">
              Beskrivning
            </DialogDescription>
          </DialogHeader>
        </DialogContent>
      </Dialog>,
    );

    const desc = classOf('[data-slot="dialog-description"]');
    expect(desc).toMatch(SIZE_CLASS);
    expect(desc).toContain("text-text-primary");
  });

  it("keeps the description's size when the caller passes no className at all", () => {
    // The narrower reading — "the caller's class wins" — would be satisfied by a
    // primitive that still drops the size here. It did drop it here.
    render(
      <Dialog open>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Titel</DialogTitle>
            <DialogDescription>Beskrivning</DialogDescription>
          </DialogHeader>
        </DialogContent>
      </Dialog>,
    );

    expect(classOf('[data-slot="dialog-description"]')).toMatch(SIZE_CLASS);
  });

  it("keeps the title's size the same way — same defect, same mechanism", () => {
    render(
      <Dialog open>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Titel</DialogTitle>
            <DialogDescription>Beskrivning</DialogDescription>
          </DialogHeader>
        </DialogContent>
      </Dialog>,
    );

    expect(classOf('[data-slot="dialog-title"]')).toMatch(SIZE_CLASS);
  });

  it("still lets a caller set the size deliberately", () => {
    render(
      <Dialog open>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Titel</DialogTitle>
            <DialogDescription className="text-body-lg">
              Beskrivning
            </DialogDescription>
          </DialogHeader>
        </DialogContent>
      </Dialog>,
    );

    expect(classOf('[data-slot="dialog-description"]')).toContain("text-body-lg");
  });

  it("gives InfoDialog's two same-rank paragraphs one size class", async () => {
    render(<InfoDialog title="Om hjälpen" paragraphs={["Första", "Andra"]} />);
    fireEvent.click(screen.getByRole("button", { name: "Vad är detta?" }));
    await waitFor(() => expect(screen.getByRole("dialog")).toBeInTheDocument());

    const desc = document.querySelector('[data-slot="dialog-description"]');
    expect(desc?.textContent).toBe("Första");

    // Paragraph 2 renders outside `cn`, so its `text-body-sm` was never at risk; the
    // rank split was paragraph 1 losing its size. Assert the wrapper still carries the
    // 14px rung, so the two are compared against the same intended size.
    const rest = document.querySelector('[data-slot="dialog-content"] > div:last-of-type');
    expect(rest?.getAttribute("class")).toContain("text-body-sm");
    expect(rest?.textContent).toBe("Andra");

    expect(desc?.getAttribute("class")).toMatch(SIZE_CLASS);
  });
});
