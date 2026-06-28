import { describe, it, expect, vi, afterEach } from "vitest";
import { useState } from "react";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
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

// E2h/E2i-harness: selectOnTab aktiv, plus ett efterföljande fokuserbart
// element så Tab-fokus-flytt kan asserteras.
function SelectOnTabHarness({
  onSelect,
}: {
  onSelect: (s: SuggestionDto) => void;
}) {
  const [value, setValue] = useState("");
  return (
    <>
      <JobAdTypeahead
        id="q"
        value={value}
        onChange={setValue}
        onSelect={onSelect}
        selectOnTab
      />
      <button type="button">Nästa fält</button>
    </>
  );
}

// #295 harness: the typeahead inside a <form> so a committed search (Enter with
// no marked suggestion) can be asserted — Enter must close the popup AND bubble
// to the form's submit (the free search runs). A submit button is present so
// userEvent's implicit form submission fires.
function FormHarness({
  onSubmit,
  onSelect,
}: {
  onSubmit: () => void;
  onSelect?: (s: SuggestionDto) => void;
}) {
  const [value, setValue] = useState("");
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        onSubmit();
      }}
    >
      <JobAdTypeahead
        id="q"
        value={value}
        onChange={setValue}
        onSelect={onSelect ?? (() => {})}
      />
      <button type="submit">Sök</button>
    </form>
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

  it("Tab is NOT intercepted without a marked option (no focus trap)", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(
          JSON.stringify([{ kind: 0, conceptId: null, label: "Frontend" }]),
          { status: 200 },
        ),
    );
    vi.stubGlobal("fetch", fetchMock);
    const onSelect = vi.fn();
    const user = userEvent.setup();

    render(
      <SelectOnTabHarness onSelect={onSelect} />,
    );
    await user.type(screen.getByRole("combobox"), "fr");
    await screen.findByRole("option", { name: "Frontend" }, { timeout: 2000 });

    // Ingen markering (active = -1) → Tab flyttar fokus normalt.
    await user.tab();
    expect(onSelect).not.toHaveBeenCalled();
    expect(screen.getByRole("combobox")).not.toHaveFocus();
  });

  it("Shift+Tab is never intercepted, even with a marked option", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(
          JSON.stringify([{ kind: 0, conceptId: null, label: "Frontend" }]),
          { status: 200 },
        ),
    );
    vi.stubGlobal("fetch", fetchMock);
    const onSelect = vi.fn();
    const user = userEvent.setup();

    render(
      <SelectOnTabHarness onSelect={onSelect} />,
    );
    await user.type(screen.getByRole("combobox"), "fr");
    await screen.findByRole("option", { name: "Frontend" }, { timeout: 2000 });
    await user.keyboard("{ArrowDown}");
    await user.tab({ shift: true });
    expect(onSelect).not.toHaveBeenCalled();
  });

  it("Tab with a marked option does nothing special WITHOUT selectOnTab (OCP — default consumers unaffected)", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(
          JSON.stringify([{ kind: 0, conceptId: null, label: "Frontend" }]),
          { status: 200 },
        ),
    );
    vi.stubGlobal("fetch", fetchMock);
    const onSelect = vi.fn();
    const user = userEvent.setup();

    render(<ControlledHarness onSelect={onSelect} />);
    await user.type(screen.getByRole("combobox"), "fr");
    await screen.findByRole("option", { name: "Frontend" }, { timeout: 2000 });
    await user.keyboard("{ArrowDown}");
    await user.tab();
    expect(onSelect).not.toHaveBeenCalled();
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

  // #295 — dismissal (WAI-ARIA APG combobox): the suggestion popup must close on
  // (1) a committed search (Enter / selected suggestion), (2) an outside pointer
  // press, (3) focus leaving the widget (Tab / blur) and (4) Escape — and reopen
  // on new input.
  function singleOptionFetch() {
    return vi.fn(
      async () =>
        new Response(
          JSON.stringify([{ kind: 0, conceptId: null, label: "AI-ingenjör" }]),
          { status: 200 },
        ),
    );
  }

  it("#295: Enter without a marked option closes the list AND commits the search", async () => {
    vi.stubGlobal("fetch", singleOptionFetch());
    const onSubmit = vi.fn();
    const onSelect = vi.fn();
    const user = userEvent.setup();

    render(<FormHarness onSubmit={onSubmit} onSelect={onSelect} />);
    await user.type(screen.getByRole("combobox"), "AI");
    await screen.findByRole(
      "option",
      { name: "AI-ingenjör" },
      { timeout: 2000 },
    );

    // No marked row (active = -1) → Enter = free search: it commits (bubbles to
    // the <form>) but must also close the popup (the core bug in #295).
    await user.keyboard("{Enter}");

    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSelect).not.toHaveBeenCalled();
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    expect(screen.getByRole("combobox")).toHaveAttribute(
      "aria-expanded",
      "false",
    );
  });

  it("#295: an outside pointer press on a focusable element closes the list", async () => {
    vi.stubGlobal("fetch", singleOptionFetch());
    const user = userEvent.setup();

    render(
      <div>
        <ControlledHarness onSelect={vi.fn()} />
        <button type="button">Utanför</button>
      </div>,
    );
    await user.type(screen.getByRole("combobox"), "AI");
    await screen.findByRole(
      "option",
      { name: "AI-ingenjör" },
      { timeout: 2000 },
    );

    await user.click(screen.getByRole("button", { name: "Utanför" }));

    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("#295: an outside pointerdown on INERT content closes the list without relying on blur (touch-safe)", async () => {
    vi.stubGlobal("fetch", singleOptionFetch());
    const user = userEvent.setup();

    render(
      <div>
        <ControlledHarness onSelect={vi.fn()} />
        <div data-testid="inert">inert page content</div>
      </div>,
    );
    const input = screen.getByRole("combobox");
    await user.type(input, "AI");
    await screen.findByRole(
      "option",
      { name: "AI-ingenjör" },
      { timeout: 2000 },
    );
    expect(input).toHaveFocus();

    // A press on non-focusable (inert) content does not move focus, so the input
    // keeps focus and no blur fires — exactly the mobile case where tapping inert
    // content must still dismiss. `pointerdown` is the modality-agnostic signal
    // (mobile browsers synthesise no mousedown for inert targets). Dismissal here
    // therefore proves the document pointerdown listener, not the onBlur path.
    fireEvent.pointerDown(screen.getByTestId("inert"));

    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    expect(input).toHaveFocus();
  });

  it("#295: focus leaving the combobox (Tab-out / blur) closes the list", async () => {
    vi.stubGlobal("fetch", singleOptionFetch());
    const user = userEvent.setup();

    render(
      <>
        <ControlledHarness onSelect={vi.fn()} />
        <button type="button">Annat fält</button>
      </>,
    );
    await user.type(screen.getByRole("combobox"), "AI");
    await screen.findByRole(
      "option",
      { name: "AI-ingenjör" },
      { timeout: 2000 },
    );

    // No selectOnTab + no marked row → Tab moves focus out of the widget.
    await user.tab();

    expect(screen.getByRole("combobox")).not.toHaveFocus();
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("#295: Escape closes the open suggestion list", async () => {
    vi.stubGlobal("fetch", singleOptionFetch());
    const user = userEvent.setup();

    render(<ControlledHarness onSelect={vi.fn()} />);
    await user.type(screen.getByRole("combobox"), "AI");
    await screen.findByRole(
      "option",
      { name: "AI-ingenjör" },
      { timeout: 2000 },
    );

    await user.keyboard("{Escape}");

    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("#295: typing again reopens the list after a committed-search dismissal", async () => {
    vi.stubGlobal("fetch", singleOptionFetch());
    const user = userEvent.setup();

    render(<FormHarness onSubmit={vi.fn()} />);
    const input = screen.getByRole("combobox");
    await user.type(input, "AI");
    await screen.findByRole(
      "option",
      { name: "AI-ingenjör" },
      { timeout: 2000 },
    );

    await user.keyboard("{Enter}");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();

    // New input → the popup reopens (AC: "reopens on new input").
    await user.type(input, "x");
    expect(
      await screen.findByRole("listbox", undefined, { timeout: 2000 }),
    ).toBeInTheDocument();
  });
});
