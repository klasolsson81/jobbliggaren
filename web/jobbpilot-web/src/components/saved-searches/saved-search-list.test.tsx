import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SavedSearchList } from "./saved-search-list";
import type { SavedSearchDto } from "@/lib/dto/saved-searches";
import type { ActionResult } from "@/lib/actions/saved-searches";

const deleteMock = vi.fn<(id: string) => Promise<ActionResult>>();

vi.mock("@/lib/actions/saved-searches", () => ({
  deleteSavedSearchAction: (id: string) => deleteMock(id),
}));

const sample = (id: string, name: string): SavedSearchDto => ({
  id,
  name,
  ssyk: ["MVqp_eS8_kDZ"],
  region: [],
  q: "java",
  sortBy: "PublishedAtDesc",
  notificationEnabled: false,
  lastRunAt: null,
  createdAt: "2026-05-16T08:00:00Z",
  updatedAt: "2026-05-16T08:00:00Z",
});

describe("SavedSearchList", () => {
  beforeEach(() => {
    deleteMock.mockReset();
    deleteMock.mockResolvedValue({ success: true });
  });

  it("renders civic empty-state when no saved searches", () => {
    render(<SavedSearchList savedSearches={[]} />);
    expect(
      screen.getByText("Du har inga sparade sökningar")
    ).toBeInTheDocument();
  });

  it("renders one row per saved search with criteria summary and Kör link", () => {
    render(<SavedSearchList savedSearches={[sample("s1", "Java SthlmA")]} />);
    expect(screen.getByText("Java SthlmA")).toBeInTheDocument();
    expect(screen.getByText(/sökord "java"/)).toBeInTheDocument();
    const link = screen.getByRole("link", { name: "Kör" });
    expect(link).toHaveAttribute("href", "/sokningar/s1");
  });

  it("does not open the confirm dialog until Radera is clicked", () => {
    render(<SavedSearchList savedSearches={[sample("s1", "Java")]} />);

    expect(
      screen.getByRole("button", { name: "Radera" })
    ).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("opens a confirmation dialog with the search name and undo warning", async () => {
    const user = userEvent.setup();
    render(<SavedSearchList savedSearches={[sample("s1", "Java")]} />);

    await user.click(screen.getByRole("button", { name: "Radera" }));

    const dialog = await screen.findByRole("dialog");
    expect(dialog).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "Radera sparad sökning?" })
    ).toBeInTheDocument();
    expect(screen.getByText(/Det går inte att ångra/)).toBeInTheDocument();
    expect(deleteMock).not.toHaveBeenCalled();
  });

  it("deletes the saved search after confirming in the dialog", async () => {
    const user = userEvent.setup();
    render(<SavedSearchList savedSearches={[sample("s1", "Java")]} />);

    await user.click(screen.getByRole("button", { name: "Radera" }));
    await screen.findByRole("dialog");
    await user.click(
      screen.getByRole("button", { name: "Bekräfta radering" })
    );

    await waitFor(() => expect(deleteMock).toHaveBeenCalledWith("s1"));
  });

  it("does not call the delete action when the dialog is cancelled", async () => {
    const user = userEvent.setup();
    render(<SavedSearchList savedSearches={[sample("s1", "Java")]} />);

    await user.click(screen.getByRole("button", { name: "Radera" }));
    await screen.findByRole("dialog");
    await user.click(screen.getByRole("button", { name: "Avbryt" }));

    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument()
    );
    expect(deleteMock).not.toHaveBeenCalled();
  });

  it("shows the server error inside the dialog on a failed delete", async () => {
    deleteMock.mockResolvedValueOnce({
      success: false,
      error: "Kunde inte radera sökningen.",
    });
    const user = userEvent.setup();
    render(<SavedSearchList savedSearches={[sample("s1", "Java")]} />);

    await user.click(screen.getByRole("button", { name: "Radera" }));
    await screen.findByRole("dialog");
    await user.click(
      screen.getByRole("button", { name: "Bekräfta radering" })
    );

    expect(
      await screen.findByText("Kunde inte radera sökningen.")
    ).toBeInTheDocument();
  });
});
