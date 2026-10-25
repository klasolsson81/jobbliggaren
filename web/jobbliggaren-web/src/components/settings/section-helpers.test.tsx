import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CheckItem } from "./section-helpers";

describe("CheckItem — the facet count (#2198 PR 2)", () => {
  it("shows no count and keeps the label as the whole name when none is passed, in both forms", () => {
    const { container } = render(
      <div>
        <CheckItem label="Systemutvecklare" checked={false} onToggle={vi.fn()} />
        <CheckItem label="Barnsjuksköterskor" secondary="träff på Barnsjuksköterska" checked={false} onToggle={vi.fn()} />
      </div>,
    );

    expect(container.querySelector(".jp-checkitem__count")).toBeNull();
    expect(screen.getByRole("checkbox", { name: "Systemutvecklare" })).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Barnsjuksköterskor" })).toBeInTheDocument();
  });

  // A one-line row is named by its content, as the popover's own rows are. jsdom applies no CSS, so
  // the flex items stay inline and the name has no space before the count; a browser adds one.
  it("makes the count part of a one-line row's name", () => {
    render(<CheckItem label="Systemutvecklare" checked={false} count={12} onToggle={vi.fn()} />);

    const row = screen.getByRole("checkbox", { name: /^Systemutvecklare\s?\(12\)$/ });
    const count = row.querySelector(".jp-checkitem__count");
    expect(count).toHaveTextContent("(12)");
    expect(count?.parentElement).toBe(screen.getByText("Systemutvecklare"));
    expect(count?.parentElement).not.toBe(row);
  });

  it("makes the count part of a two-line row's name, on the first line, and keeps the second line its description", () => {
    render(
      <CheckItem
        label="Grundutbildade sjuksköterskor"
        secondary="träff på Sjuksköterska"
        checked={false}
        count={340}
        onToggle={vi.fn()}
      />,
    );

    const row = screen.getByRole("checkbox", { name: "Grundutbildade sjuksköterskor (340)" });
    expect(row).toHaveAccessibleDescription("träff på Sjuksköterska");
    const count = row.querySelector(".jp-checkitem__count");
    const name = screen.getByText("Grundutbildade sjuksköterskor");
    expect(count?.parentElement).toBe(name.parentElement);
  });

  it("shows a known zero, and formats thousands as the popover does", () => {
    render(
      <div>
        <CheckItem label="Gravörer" checked={false} count={0} onToggle={vi.fn()} />
        <CheckItem label="Chefer" secondary="träff på Chef" checked={false} count={1500} onToggle={vi.fn()} />
      </div>,
    );

    expect(screen.getByRole("checkbox", { name: /^Gravörer\s?\(0\)$/ })).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: /^Chefer \(1\s500\)$/ })).toBeInTheDocument();
  });
});

describe("CheckItem — the included state (#2199)", () => {
  it("is checked, aria-disabled, still focusable, and says why in its description", () => {
    render(
      <CheckItem
        label="Projektledare, IT"
        secondary="Ingår i vald yrkesgrupp: Mjukvaru- och systemutvecklare m.fl."
        checked={false}
        included
        onToggle={vi.fn()}
      />,
    );

    const row = screen.getByRole("checkbox", { name: "Projektledare, IT" });
    expect(row).toHaveAttribute("aria-checked", "true");
    expect(row).toHaveAttribute("aria-disabled", "true");
    expect(row).toHaveAttribute("tabindex", "0");
    expect(row).toHaveClass("jp-checkitem--included");
    expect(row).toHaveAccessibleDescription(
      "Ingår i vald yrkesgrupp: Mjukvaru- och systemutvecklare m.fl.",
    );
    expect(row.querySelector(".jp-checkitem__box svg")).not.toBeNull();
  });

  it("ignores a click, Space and Enter", async () => {
    const user = userEvent.setup();
    const onToggle = vi.fn();
    render(
      <CheckItem
        label="Projektledare, IT"
        secondary="Ingår i vald yrkesgrupp: Mjukvaru- och systemutvecklare m.fl."
        checked
        included
        onToggle={onToggle}
      />,
    );

    const row = screen.getByRole("checkbox", { name: "Projektledare, IT" });
    await user.click(row);
    row.focus();
    await user.keyboard(" ");
    await user.keyboard("{Enter}");
    expect(onToggle).not.toHaveBeenCalled();
  });

  it("writes no aria-disabled and no included class without the prop, and still toggles", async () => {
    const user = userEvent.setup();
    const onToggle = vi.fn();
    render(
      <CheckItem
        label="Projektledare, IT"
        secondary="Yrkesgrupp: Mjukvaru- och systemutvecklare m.fl."
        checked={false}
        onToggle={onToggle}
      />,
    );

    const row = screen.getByRole("checkbox", { name: "Projektledare, IT" });
    expect(row).not.toHaveAttribute("aria-disabled");
    expect(row).not.toHaveClass("jp-checkitem--included");
    await user.click(row);
    expect(onToggle).toHaveBeenCalledTimes(1);
  });
});
