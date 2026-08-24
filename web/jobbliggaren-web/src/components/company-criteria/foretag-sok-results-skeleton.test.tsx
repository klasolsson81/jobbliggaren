import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../messages/sv/pages.json";
import { Announcer } from "@/components/common/announcer";
import { ForetagSokResultsSkeleton } from "./foretag-sok-results-skeleton";

const region = (c: HTMLElement) => c.querySelector('p[role="status"]');

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
      <Announcer>
        <ForetagSokResultsSkeleton />
      </Announcer>,
    );

    // Read through the real catalogue, so a renamed or deleted key fails here rather than
    // silently announcing a raw message id.
    expect(region(container)).toHaveTextContent(t("foretag.sok.loadingResults"));
  });
});
