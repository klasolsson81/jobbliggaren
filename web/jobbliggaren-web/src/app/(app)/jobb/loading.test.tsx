import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { renderToString } from "react-dom/server";
import { NextIntlClientProvider } from "next-intl";
import messages from "../../../../messages/sv";
import Loading from "./loading";

/**
 * #1505 — the CALL SITE of the live region on the route-level loading path.
 *
 * Same reason as `page.test.tsx`'s block: `Announce` is inert without a provider by design, so
 * removing `<Announcer>` from this file leaves no type error, no runtime error and every other
 * test green while a cross-route navigation to `/jobb` announces nothing.
 *
 * This region is its OWN node, not the one `page.tsx` mounts — a cross-route navigation swaps the
 * whole subtree, so the opening sentence lands here and the closing one lands there. Both are empty
 * when they mount, which is what ARIA22 asks of each; the criterion is about ordering within a
 * region, not about one region spanning a whole cycle.
 */
describe("/jobb loading.tsx — the skeleton has a region to announce through", () => {
  it("puts the sentence in the region AND leaves it visible", () => {
    const { container } = render(<Loading />);

    const live = container.querySelector('p[role="status"][aria-live="polite"]');
    expect(live).not.toBeNull();
    expect(live).toHaveAttribute("aria-atomic", "true");
    // The region actually receives it on this path too — `useEffect` runs after the region is
    // committed, so the ordering ARIA22 requires holds even though both mount in one commit.
    expect(live).toHaveTextContent("Söker bland annonser…");

    // Two elements carry the sentence by design: the region announces it, the visible line shows
    // it. `getAllByText` rather than `getByText` for exactly that reason — a sighted user must not
    // lose the text just because it is also announced.
    const visible = screen
      .getAllByText("Söker bland annonser…")
      .filter((el) => !el.classList.contains("sr-only"));
    expect(visible).toHaveLength(1);
    // …and the visible one is ordinary content, not a second live region.
    expect(visible[0]).not.toHaveAttribute("role");
    expect(visible[0]).not.toHaveAttribute("aria-live");
  });

  it("ships the region EMPTY in the server HTML, before any effect has run", () => {
    // The half no client render can measure. `useEffect` does not run on the server, so the first
    // bytes a browser receives are the region's true initial state — and ARIA22 is about exactly
    // that: the container must hold its role BEFORE the message occurs. Every other assertion in
    // this file runs after the effect and therefore cannot tell "empty then filled" from "born
    // filled".
    //
    // The mutation this exists for (`test-writer`, the tenth): give `Announcer` an
    // `initialMessage` prop and have THIS file pass its opening sentence. Every client-side pin
    // still passes, the e2e spec still passes — it measures node identity across an IN-PAGE
    // search — and the route-level path is back in precisely the defect this PR closes.
    //
    // It has to be rendered HERE, not against `Announcer` alone: an isolated render never passes
    // the prop, so it is blind to the host that would. Measured — the first version of this pin
    // lived in `announcer.test.tsx` and the whole mutation survived it.
    //
    // Same shape and reason as `foretag-sok-searchbar.test.tsx`'s pre-hydration assertion, the
    // repo's other `react-dom/server` pin.
    const html = renderToString(
      <NextIntlClientProvider
        locale="sv"
        messages={messages}
        timeZone="Europe/Stockholm"
      >
        <Loading />
      </NextIntlClientProvider>,
    );

    // Parse the region OUT and assert it is empty, rather than asserting the absence of one
    // particular sentence. Measured why: a version of this pin checked that
    // "Söker bland annonser…" occurred exactly once, and the mutation survived it by seeding the
    // region with a DIFFERENT string. What the criterion requires is that the container is empty,
    // whatever the message would have been.
    // `[\s\S]` rather than the `s` flag: the tsconfig target predates es2018 (TS1501).
    const region = /<p[^>]*aria-atomic="true"[^>]*>([\s\S]*?)<\/p>/.exec(html);
    expect(region).not.toBeNull();
    expect(region?.[1]).toBe("");
    // …and it really is the announcer's region, not some other atomic element.
    expect(region?.[0]).toContain('role="status"');
    expect(region?.[0]).toContain('aria-live="polite"');
    // The fourth mutation, which the emptiness assertion alone does NOT see: seed the opening
    // sentence as an `aria-label` on the region instead of as text. `role=status` is
    // `nameFrom: author`, so it is read out while the text content stays "" and every assertion
    // above passes. `test-writer` deduced it; measured here before fixing — an `initialLabel`
    // prop passed from this file survived all eight tests in the two suites.
    expect(region?.[0]).not.toContain("aria-label");
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
