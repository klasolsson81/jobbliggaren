import { describe, it, expect, vi, afterEach } from "vitest";
import { useState } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobAdTypeahead } from "./job-ad-typeahead";
import type { SuggestionDto } from "@/lib/dto/job-ads";

function ControlledHarness({
  onSelect,
}: {
  onSelect: (s: SuggestionDto) => void;
}) {
  // Liten controlled-wrapper — komponenten är controlled (value/onChange).
  const [value, setValue] = useState("");
  return (
    <JobAdTypeahead
      id="q"
      value={value}
      onChange={setValue}
      onSelect={onSelect}
    />
  );
}

/**
 * Real timers (ingen fake) — debouncen är 300ms; waitFor med generös timeout
 * täcker det utan fake-timer/userEvent-deadlock.
 */
describe("JobAdTypeahead (ADR 0042 Beslut C + ADR 0067 Fas E2d)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("does not call the suggest endpoint for prefixes under 2 chars", async () => {
    const fetchMock = vi.fn(async () => new Response("[]", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    render(<ControlledHarness onSelect={vi.fn()} />);
    await user.type(screen.getByRole("combobox"), "a");

    await new Promise((r) => setTimeout(r, 500));
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("debounces then fetches suggestions and renders them as options", async () => {
    const fetchMock = vi.fn(
      async () =>
        // kind serialiseras som heltal (0=Title), conceptId=null för titel.
        new Response(
          JSON.stringify([
            { kind: 0, conceptId: null, label: "Backend-utvecklare" },
          ]),
          { status: 200 },
        ),
    );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    render(<ControlledHarness onSelect={vi.fn()} />);
    await user.type(screen.getByRole("combobox"), "back");

    expect(
      await screen.findByRole(
        "option",
        { name: "Backend-utvecklare" },
        { timeout: 2000 },
      ),
    ).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/api/jobb/suggest?prefix=back"),
      expect.anything(),
    );
  });

  it("selecting a suggestion calls onSelect with the full SuggestionDto", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(
          JSON.stringify([
            // kind=2 = Municipality (ADR 0067 wire-ordningen), conceptId satt.
            { kind: 2, conceptId: "PVZL_BQT_XtL", label: "Göteborg" },
          ]),
          { status: 200 },
        ),
    );
    vi.stubGlobal("fetch", fetchMock);
    const onSelect = vi.fn();
    const user = userEvent.setup();

    render(<ControlledHarness onSelect={onSelect} />);
    await user.type(screen.getByRole("combobox"), "göte");

    const option = await screen.findByRole(
      "option",
      { name: "Göteborg" },
      { timeout: 2000 },
    );
    await user.click(option);

    // Hela DTO:n vidare (kind→dimension-mappning är förälderns ansvar, E2d).
    expect(onSelect).toHaveBeenCalledWith({
      kind: "Municipality",
      conceptId: "PVZL_BQT_XtL",
      label: "Göteborg",
    });
  });

  it("keyboard ArrowDown + Enter selects the active option (a11y combobox)", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(
          JSON.stringify([
            { kind: 0, conceptId: null, label: "Frontend-utvecklare" },
            { kind: 0, conceptId: null, label: "Fullstack-utvecklare" },
          ]),
          { status: 200 },
        ),
    );
    vi.stubGlobal("fetch", fetchMock);
    const onSelect = vi.fn();
    const user = userEvent.setup();

    render(<ControlledHarness onSelect={onSelect} />);
    const input = screen.getByRole("combobox");
    await user.type(input, "f");
    await user.type(input, "u");
    await screen.findByRole(
      "option",
      { name: "Frontend-utvecklare" },
      { timeout: 2000 },
    );

    // Pil ned två gånger → andra raden markerad (aria-activedescendant).
    await user.keyboard("{ArrowDown}{ArrowDown}");
    expect(
      screen.getByRole("option", { name: "Fullstack-utvecklare" }),
    ).toHaveAttribute("aria-selected", "true");

    await user.keyboard("{Enter}");
    expect(onSelect).toHaveBeenCalledWith({
      kind: "Title",
      conceptId: null,
      label: "Fullstack-utvecklare",
    });
  });

  it("handles a 429 rateLimited response civilly", async () => {
    const fetchMock = vi.fn(async () => new Response("[]", { status: 429 }));
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    render(<ControlledHarness onSelect={vi.fn()} />);
    await user.type(screen.getByRole("combobox"), "java");

    await waitFor(
      () =>
        expect(
          screen.getByText(/För många sökningar på kort tid/),
        ).toBeInTheDocument(),
      { timeout: 2000 },
    );
  });
});
