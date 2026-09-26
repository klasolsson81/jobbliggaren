import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import messages from "../../../../messages/sv";
import Loading from "./loading";

// #1741 PR B (ADR 0142 D7): the page's hero lost its kicker, so the fallback reserves no kicker row
// and renders the page's own static title (#1385), which keeps the band one height on swap.
describe("/oversikt loading.tsx — the hero fallback", () => {
  it("is exactly the page's title, with no kicker row and no lede", () => {
    const { container } = render(<Loading />);

    const main = container.querySelector<HTMLElement>(".jp-pagehero__main");
    expect(main).not.toBeNull();
    expect([...main!.children].map((c) => c.tagName)).toEqual(["H1"]);
    expect(main!.textContent).toBe(messages.oversikt.hero.title);
  });
});
