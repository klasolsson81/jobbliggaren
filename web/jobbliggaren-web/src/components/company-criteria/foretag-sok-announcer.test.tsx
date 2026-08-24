import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../messages/sv/pages.json";
import { ForetagSokAnnouncer, Announce } from "./foretag-sok-announcer";
import { ForetagSokResultsSkeleton } from "./foretag-sok-results-skeleton";

/**
 * #1092 — the mechanism that makes `/foretag/sok`'s load cycle announceable (WCAG 4.1.3).
 *
 * What is actually at stake is not "is there a live region" — there always was one, on the element
 * that rendered the text. It is whether the region exists BEFORE the message does, which is what
 * ARIA22's test procedure requires and what the old shape could never satisfy: the skeleton IS the
 * Suspense fallback, so its region was born holding its own sentence.
 *
 * So the load-bearing assertion here is NODE IDENTITY across a content swap. A region that is the
 * same DOM node before and after the subtree changes is one an assistive technology has already
 * registered; a region re-created with each swap is the defect this closes, and it would pass any
 * assertion that only looked for `role="status"` in the output.
 */

const region = (c: HTMLElement) => c.querySelector('p[role="status"]');

describe("ForetagSokAnnouncer — the region precedes the message", () => {
  it("is in the DOM and EMPTY with no announcement at all", () => {
    const { container } = render(
      <ForetagSokAnnouncer>
        <p>results</p>
      </ForetagSokAnnouncer>,
    );

    const live = region(container);
    expect(live).not.toBeNull();
    expect(live).toHaveAttribute("aria-live", "polite");
    // `aria-atomic` so a swapped sentence is read whole rather than diffed word by word.
    expect(live).toHaveAttribute("aria-atomic", "true");
    expect(live).toHaveTextContent("");
  });

  it("survives a content swap as the SAME node", () => {
    const { container, rerender } = render(
      <ForetagSokAnnouncer>
        <p>loading</p>
      </ForetagSokAnnouncer>,
    );
    const before = region(container);

    rerender(
      <ForetagSokAnnouncer>
        <p>settled</p>
      </ForetagSokAnnouncer>,
    );

    expect(screen.getByText("settled")).toBeInTheDocument();
    // Identity, not presence: a region rebuilt with each swap is exactly the bug being fixed.
    expect(region(container)).toBe(before);
  });

  it("carries the sentence a mounted Announce gives it", () => {
    const { container } = render(
      <ForetagSokAnnouncer>
        <Announce message="Söker företag…" />
      </ForetagSokAnnouncer>,
    );

    expect(region(container)).toHaveTextContent("Söker företag…");
  });

  /**
   * A later sentence replaces the one before it. That is the whole contract, and it is deliberately
   * stated as a contract rather than as a claim about what a user hears.
   *
   * An earlier version of this file pinned a cleanup blank instead, asserting that it kept two
   * identical consecutive counts audible. `code-reviewer` measured that false (Blocker 1, PR #1504):
   * React runs passive unmount and mount effects in the same flush, so a blank written by a
   * departing subtree is batched away and never reaches the DOM. The only sequence in which it did
   * anything was the test harness's own — a `src/` path produces no such sequence, which is the §5
   * `Tests:` violation. The blank is gone and the claim with it; what actually keeps a repeated
   * count audible is the skeleton's differing sentence between the two, which the surface cases
   * cover.
   */
  it("replaces the sentence when a later Announce supersedes it", () => {
    const { container, rerender } = render(
      <ForetagSokAnnouncer>
        <Announce message="Söker företag…" />
      </ForetagSokAnnouncer>,
    );
    expect(region(container)).toHaveTextContent("Söker företag…");

    rerender(
      <ForetagSokAnnouncer>
        <Announce message="1 234 träffar" />
      </ForetagSokAnnouncer>,
    );
    expect(region(container)).toHaveTextContent("1 234 träffar");
  });

  it("is inert without a surrounding region rather than throwing", () => {
    // Not a convenience: it keeps the skeleton renderable by any future host, and a throw here
    // would take down a loading state rather than degrade one announcement.
    expect(() => render(<Announce message="orphaned" />)).not.toThrow();
  });
});

/**
 * The skeleton's half of the same criterion. It renders the visible "Söker företag…" sentence, and
 * that sentence is a status message in WCAG's own vocabulary ("Searching…" is the Understanding
 * document's example). What changed is only WHERE it is announced from.
 */
describe("ForetagSokResultsSkeleton — announces through the region, never from itself", () => {
  const t = createTranslator({
    locale: "sv",
    messages: { pages: svPages },
    namespace: "pages",
  });

  it("keeps the visible sentence but carries NO live region of its own", () => {
    const { container } = render(<ForetagSokResultsSkeleton />);

    expect(screen.getByText("Söker företag…")).toBeInTheDocument();
    // The regression this guards: restoring `role="status"` here would re-create the unreliable
    // shape AND double the announcement once the surface region is in place.
    expect(container.querySelector('[role="status"]')).toBeNull();
    expect(container.querySelector("[aria-live]")).toBeNull();
    // `aria-busy` describes this subtree's own state and is unrelated to the announcement.
    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
  });

  it("puts that same sentence into the surface region when hosted by one", () => {
    const { container } = render(
      <ForetagSokAnnouncer>
        <ForetagSokResultsSkeleton />
      </ForetagSokAnnouncer>,
    );

    // Read through the real catalogue, so a renamed or deleted key fails here rather than
    // silently announcing a raw message id.
    expect(region(container)).toHaveTextContent(t("foretag.sok.loadingResults"));
  });
});
