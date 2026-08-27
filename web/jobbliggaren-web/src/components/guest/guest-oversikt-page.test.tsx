import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { GuestOversiktPage } from "./guest-oversikt-page";

/**
 * #1516 — the overview's `Senast uppdaterat CV` renders the product's form.
 *
 * Fourth render site, and the second one the issue did not count. It is special
 * in one way: the row sits next to `Demo aktiv sedan`, which takes its `idag`
 * from `guest.oversikt.timeToday` rather than from data. Both render `idag`
 * today, and that is a coincidence — the CTO bind of 2026-08-27 left the second
 * one alone because it carries a chosen word, not a derived phrase.
 *
 * So this test asserts on the row, not on the presence of the word `idag`: an
 * assertion on the word alone would pass even if this site stopped rendering.
 *
 * `getNodeText` reads direct text nodes only, so the match is the label
 * `<span>` and `parentElement` is the row — deliberately not the group, which
 * also contains `Demo aktiv sedan`.
 */
describe("GuestOversiktPage — relative time (#1516)", () => {
  it("Senast uppdaterat CV renders the derived form", () => {
    render(<GuestOversiktPage />);

    // gr-1 sits on the reference date -> `idag`, derived via formatDaysAgo.
    const row = screen.getByText("Senast uppdaterat CV").parentElement;
    expect(row?.textContent).toContain("idag");
  });

  it("renders no `för …` form anywhere on the page", () => {
    const { container } = render(<GuestOversiktPage />);

    expect(container.textContent).not.toMatch(/för \d/);
  });
});
