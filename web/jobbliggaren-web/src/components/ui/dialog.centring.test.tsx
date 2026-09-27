import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";

import { Dialog, DialogContent, DialogDescription, DialogTitle } from "./dialog";

/**
 * #1850 contract guard. `anchoredDialogStyle` (lib/applications/anchored-top.ts)
 * places the /ansokningar action dialogs by overriding the `translate` property
 * that this primitive centres with. If the primitive stops centring through
 * Tailwind's translate utilities (a shadcn re-sync, say), that override no longer
 * replaces the centring and the dialog moves twice again, with no other test red.
 */
describe("Dialog centring contract (#1850)", () => {
  it("centres the content with the translate utilities anchoredDialogStyle overrides", () => {
    render(
      <Dialog open>
        <DialogContent>
          <DialogTitle>Titel</DialogTitle>
          <DialogDescription>Beskrivning</DialogDescription>
        </DialogContent>
      </Dialog>,
    );

    const classes = (
      document.querySelector('[data-slot="dialog-content"]')?.getAttribute("class") ?? ""
    ).split(/\s+/);

    expect(classes).toEqual(
      expect.arrayContaining(["-translate-x-1/2", "-translate-y-1/2"]),
    );
  });
});
