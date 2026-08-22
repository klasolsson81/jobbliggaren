import { describe, it, expect } from "vitest";
import type { ReactNode } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Clock } from "lucide-react";
import { HeroChip } from "./hero-chip";

type Row = { id: string; label: string };

function renderChip(props?: {
  items?: Array<Row>;
  count?: number | null;
  maxItems?: number;
  footerHref?: string;
  renderTrailing?: (item: Row) => ReactNode;
}) {
  const {
    items = [
      { id: "a1", label: "backend" },
      { id: "a2", label: "designer" },
    ],
    count = 2,
    maxItems = 5,
    footerHref = "/sokningar",
    renderTrailing,
  } = props ?? {};
  return render(
    <HeroChip
      label="Senaste sökningar"
      icon={<Clock size={14} aria-hidden="true" />}
      count={count}
      items={items}
      getKey={(it) => it.id}
      getHref={(it) => `/jobb?q=${it.label}`}
      getLabel={(it) => it.label}
      renderTrailing={renderTrailing}
      emptyText="Inga senaste sökningar än."
      footerHref={footerHref}
      footerLabel="Visa alla"
      maxItems={maxItems}
    />,
  );
}

describe("HeroChip", () => {
  it("renders the trigger with label, count in parens, and stays closed by default", () => {
    renderChip();
    const trigger = screen.getByRole("button", { name: /Senaste sökningar/ });
    expect(trigger).toBeInTheDocument();
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByText("(2)")).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("omits the count in parens when count is null", () => {
    renderChip({ count: null });
    expect(screen.queryByText(/^\(/)).not.toBeInTheDocument();
  });

  it("opens dropdown on click + closes on second click (toggle)", async () => {
    const user = userEvent.setup();
    renderChip();
    const trigger = screen.getByRole("button", { name: /Senaste sökningar/ });
    await user.click(trigger);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(trigger).toHaveAttribute("aria-expanded", "true");
    await user.click(trigger);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("renders empty-text when items is empty", async () => {
    const user = userEvent.setup();
    renderChip({ items: [], count: null });
    await user.click(screen.getByRole("button", { name: /Senaste sökningar/ }));
    expect(screen.getByText("Inga senaste sökningar än.")).toBeInTheDocument();
  });

  it("caps visible items at maxItems and offers a footer 'Visa alla' link", async () => {
    const user = userEvent.setup();
    const items = Array.from({ length: 10 }, (_, i) => ({
      id: `id-${i}`,
      label: `rad ${i}`,
    }));
    renderChip({ items, count: 10, maxItems: 5 });
    await user.click(screen.getByRole("button", { name: /Senaste sökningar/ }));

    const dialog = screen.getByRole("dialog");
    expect(dialog).toBeInTheDocument();
    expect(screen.getByText("rad 0")).toBeInTheDocument();
    expect(screen.getByText("rad 4")).toBeInTheDocument();
    expect(screen.queryByText("rad 5")).not.toBeInTheDocument();

    const footer = screen.getByRole("link", { name: "Visa alla" });
    expect(footer.getAttribute("href")).toBe("/sokningar");
  });

  it("closes the dropdown on Escape (jobbliggaren-design-a11y / ADR 0047)", async () => {
    const user = userEvent.setup();
    renderChip();
    const trigger = screen.getByRole("button", { name: /Senaste sökningar/ });
    await user.click(trigger);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("does NOT render any 'NY'-pill in dropdown rows (Klas-direktiv anti-AI-trope)", async () => {
    const user = userEvent.setup();
    const { container } = renderChip();
    await user.click(screen.getByRole("button", { name: /Senaste sökningar/ }));
    expect(container.querySelector(".jp-pill--success")).toBeNull();
    expect(container.querySelector(".jp-job__newflag")).toBeNull();
    expect(screen.queryByText(/^NY$/)).not.toBeInTheDocument();
  });

  it("owns the row shell, so a consumer's trailing slot cannot reach the label", async () => {
    const user = userEvent.setup();
    // The trailing slot is the consumer's only markup, and it may legitimately
    // truncate (saved-job-ads' company badge, maxWidth 140). Handing it the
    // failing property measures the label against the worst input a consumer
    // can supply rather than a cooperative one.
    const { container } = renderChip({
      renderTrailing: (it) => <span className="truncate">{it.id}</span>,
    });
    await user.click(screen.getByRole("button", { name: /Senaste sökningar/ }));

    // One pin here replaces the per-consumer pair this refactor deleted: with
    // the markup emitted by the host, no consumer has row markup to get wrong.
    // jsdom resolves no CSS, so this states what the markup asks for;
    // globals-popover-clamp.test.ts owns what the rule then does.
    const rows = container.querySelectorAll("a.jp-popover__rowbtn");
    expect(rows, "the row link is the host's, one per visible item").toHaveLength(2);
    // The element type is the property, not a detail of the selector above: a
    // `<button>` + `router.push` renders identically and passes every other
    // assertion here while losing ctrl-/middle-click and the link role.
    expect(rows[0]).toHaveAttribute("href", "/jobb?q=backend");

    const labelSpan = screen.getByText("backend");
    expect(labelSpan.parentElement).toBe(rows[0]);
    expect(labelSpan).toHaveClass("jp-popover__rowlabel");
    // `truncate` sets white-space: nowrap and defeats the clamp with the class
    // still on the element.
    expect(labelSpan).not.toHaveClass("truncate");
  });
});
