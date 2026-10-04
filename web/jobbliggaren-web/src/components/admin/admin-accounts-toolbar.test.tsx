import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AdminAccountsToolbar } from "./admin-accounts-toolbar";
import { AdminAccountsPager, AdminAccountsSummary } from "./admin-accounts-pager";

const COUNTS = { all: 12, active: 8, pendingDeletion: 1, profileMissing: 3 } as const;

describe("AdminAccountsToolbar (ADR 0150 D2/D3)", () => {
  it("while unbuilt, disables the search and the filter and points both to the Kommer snart line", () => {
    render(<AdminAccountsToolbar filter="all" soonId="soon" />);

    const search = screen.getByRole("searchbox", { name: "Sök på e-postadress" });
    expect(search).toBeDisabled();
    expect(search).toHaveAttribute("aria-describedby", "soon");
    const filter = screen.getByRole("radiogroup", { name: "Visa konton" });
    expect(filter).toHaveAttribute("aria-describedby", "soon");
    expect(within(filter).getAllByRole("radio").map((radio) => radio.textContent)).toEqual([
      "Alla",
      "Aktiva",
      "Under radering",
      "Ofullständiga",
    ]);
    for (const radio of within(filter).getAllByRole("radio")) expect(radio).toBeDisabled();
    // Nothing in a disabled group takes focus: no option, and no box around it.
    expect(filter.parentElement).not.toHaveAttribute("tabindex");
    expect(screen.queryByRole("region")).toBeNull();
  });

  it("searches by address on each keystroke and filters by status", async () => {
    const onQueryChange = vi.fn();
    const onFilterChange = vi.fn();
    render(
      <AdminAccountsToolbar
        filter="all"
        query=""
        onQueryChange={onQueryChange}
        onFilterChange={onFilterChange}
      />,
    );

    const search = screen.getByRole("searchbox", { name: "Sök på e-postadress" });
    expect(search).toBeEnabled();
    expect(search).not.toHaveAttribute("aria-describedby");
    await userEvent.type(search, "ko");
    expect(onQueryChange.mock.calls).toEqual([["k"], ["o"]]);

    expect(screen.getByRole("radio", { name: "Alla" })).toHaveAttribute("aria-checked", "true");
    await userEvent.click(screen.getByRole("radio", { name: "Ofullständiga" }));
    expect(onFilterChange).toHaveBeenCalledWith("profileMissing");
  });

  it("keeps a typed address out of the browser's form history and its spelling service", () => {
    render(<AdminAccountsToolbar filter="all" query="" onQueryChange={() => {}} onFilterChange={() => {}} />);

    const search = screen.getByRole("searchbox", { name: "Sök på e-postadress" });
    expect(search).toHaveAttribute("autocomplete", "off");
    expect(search).toHaveAttribute("spellcheck", "false");
  });

  it("shows the options its caller names, in their order", () => {
    render(
      <AdminAccountsToolbar
        filter="all"
        filters={["all", "active", "suspended", "pendingDeletion", "profileMissing"]}
        onQueryChange={() => {}}
        onFilterChange={() => {}}
      />,
    );
    expect(screen.getAllByRole("radio").map((radio) => radio.textContent)).toEqual([
      "Alla",
      "Aktiva",
      "Suspenderade",
      "Under radering",
      "Ofullständiga",
    ]);
  });

  it("shows a count in each filter label only when the counts are known", () => {
    const { unmount } = render(
      <AdminAccountsToolbar filter="active" onQueryChange={() => {}} onFilterChange={() => {}} counts={COUNTS} />,
    );
    expect(screen.getByRole("radio", { name: "Aktiva (8)" })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("radio", { name: "Under radering (1)" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Ofullständiga (3)" })).toBeInTheDocument();
    unmount();

    render(<AdminAccountsToolbar filter="active" onQueryChange={() => {}} onFilterChange={() => {}} />);
    expect(screen.getByRole("radio", { name: "Aktiva" })).toBeInTheDocument();
  });
});

describe("AdminAccountsPager", () => {
  it("offers no pager for a single page", () => {
    render(<AdminAccountsPager page={1} pages={1} onPage={() => {}} />);
    expect(screen.queryByRole("navigation")).toBeNull();
  });

  it("says where the reader is and never offers a page that does not exist", async () => {
    const onPage = vi.fn();
    const { rerender } = render(<AdminAccountsPager page={1} pages={3} onPage={onPage} />);

    const nav = screen.getByRole("navigation", { name: "Sidnavigering" });
    expect(nav).toHaveTextContent("Sida 1 av 3");
    expect(within(nav).getByRole("button", { name: "Föregående" })).toBeDisabled();
    await userEvent.click(within(nav).getByRole("button", { name: "Nästa" }));
    expect(onPage).toHaveBeenCalledWith(2);

    rerender(<AdminAccountsPager page={3} pages={3} onPage={onPage} />);
    expect(screen.getByRole("button", { name: "Nästa" })).toBeDisabled();
    await userEvent.click(screen.getByRole("button", { name: "Föregående" }));
    expect(onPage).toHaveBeenLastCalledWith(2);
  });
});

describe("AdminAccountsSummary", () => {
  it("announces how many accounts the search and filter left", () => {
    render(<AdminAccountsSummary shown={8} total={12} />);
    expect(screen.getByRole("status")).toHaveTextContent("8 av 12 konton");
  });
});
