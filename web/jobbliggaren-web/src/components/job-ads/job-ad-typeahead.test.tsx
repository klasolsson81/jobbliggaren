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
            { kind: 0, conceptId: null, label: "Backend-utvecklare", organizationNumber: null, adCount: null, isProtectedIdentity: false },
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

  it("renders an employer row with its active ad count (#1546)", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(
          JSON.stringify([
            {
              kind: 5,
              conceptId: null,
              label: "Volvo Group AB",
              organizationNumber: "5560125790",
              adCount: 136,
              isProtectedIdentity: false,
            },
          ]),
          { status: 200 },
        ),
    );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    render(<ControlledHarness onSelect={vi.fn()} />);
    await user.type(screen.getByRole("combobox"), "volvo");

    // The whole li is the option's accessible name, so the count is announced with
    // the name rather than being decoration a screen reader never reaches.
    expect(
      await screen.findByRole(
        "option",
        { name: /Volvo Group AB\s*136 annonser/ },
        { timeout: 2000 },
      ),
    ).toBeInTheDocument();
  });

  it("renders TWO employers that share a name (#1546 React-key collision)", async () => {
    // The Volvo×20 trap, as a rendering hazard: two DISTINCT legal entities can carry
    // the same company_name. Keyed on the label they would collide; keyed on org.nr they
    // do not. A happy-path single-row test cannot see this.
    const fetchMock = vi.fn(
      async () =>
        new Response(
          JSON.stringify([
            {
              kind: 5,
              conceptId: null,
              label: "Nordic Bygg AB",
              organizationNumber: "5560125790",
              adCount: 9,
              isProtectedIdentity: false,
            },
            {
              kind: 5,
              conceptId: null,
              label: "Nordic Bygg AB",
              organizationNumber: "5569876543",
              adCount: 2,
              isProtectedIdentity: false,
            },
          ]),
          { status: 200 },
        ),
    );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    render(<ControlledHarness onSelect={vi.fn()} />);
    await user.type(screen.getByRole("combobox"), "nordic");

    await screen.findByRole("listbox", undefined, { timeout: 2000 });
    const options = screen.getAllByRole("option");
    expect(options).toHaveLength(2);
    // Distinguishable to a reader only by their counts, which is exactly why the count
    // is rendered rather than left as invisible payload.
    expect(options[0]).toHaveTextContent("9 annonser");
    expect(options[1]).toHaveTextContent("2 annonser");
  });

  it("selecting a suggestion calls onSelect with the full SuggestionDto", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(
          JSON.stringify([
            // kind=2 = Municipality (ADR 0067 wire-ordningen), conceptId satt.
            { kind: 2, conceptId: "PVZL_BQT_XtL", label: "Göteborg", organizationNumber: null, adCount: null, isProtectedIdentity: false },
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
      organizationNumber: null,
      adCount: null,
      isProtectedIdentity: false,
    });
  });

  it("keyboard ArrowDown + Enter selects the active option (a11y combobox)", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(
          JSON.stringify([
            { kind: 0, conceptId: null, label: "Frontend-utvecklare", organizationNumber: null, adCount: null, isProtectedIdentity: false },
            { kind: 0, conceptId: null, label: "Fullstack-utvecklare", organizationNumber: null, adCount: null, isProtectedIdentity: false },
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
      organizationNumber: null,
      adCount: null,
      isProtectedIdentity: false,
    });
  });

  // #1610 — the WCAG 1.4.11 active-row edge hangs on the CSS selector
  // .jp-typeahead__option[aria-selected="true"]. Asserting the class and the ARIA
  // state separately would stay green while the pair drifted apart onto different
  // elements, so this queries with the stylesheet's own selector string.
  // What is NOT assertable here: the rendered contrast. jsdom runs no cascade and
  // has no .jp-hero__plate ancestor, so the token that actually resolves on /jobb
  // is invisible to this suite — DoD #4 rendered verification carries that half.
  it("#1610: exactly one option matches the active-indicator selector, and it is the marked row", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(
          JSON.stringify([
            { kind: 0, conceptId: null, label: "Frontend-utvecklare", organizationNumber: null, adCount: null, isProtectedIdentity: false },
            { kind: 0, conceptId: null, label: "Fullstack-utvecklare", organizationNumber: null, adCount: null, isProtectedIdentity: false },
          ]),
          { status: 200 },
        ),
    );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    const { container } = render(<ControlledHarness onSelect={vi.fn()} />);
    const input = screen.getByRole("combobox");
    await user.type(input, "f");
    await user.type(input, "u");
    await screen.findByRole(
      "option",
      { name: "Frontend-utvecklare" },
      { timeout: 2000 },
    );

    const indicator = '.jp-typeahead__option[aria-selected="true"]';

    // Every option carries the hook, not just the marked one: the rule sets
    // position: relative on all of them so the pseudo-element anchors correctly.
    expect(container.querySelectorAll(".jp-typeahead__option")).toHaveLength(2);

    // Resting list — nothing marked, so the indicator selector matches nothing.
    expect(container.querySelectorAll(indicator)).toHaveLength(0);

    await user.keyboard("{ArrowDown}{ArrowDown}");

    const marked = container.querySelectorAll(indicator);
    expect(marked).toHaveLength(1);
    expect(marked[0]).toHaveTextContent("Fullstack-utvecklare");
  });

  it("Tab is NOT intercepted without a marked option (no focus trap)", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(
          JSON.stringify([{ kind: 0, conceptId: null, label: "Frontend", organizationNumber: null, adCount: null, isProtectedIdentity: false }]),
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
          JSON.stringify([{ kind: 0, conceptId: null, label: "Frontend", organizationNumber: null, adCount: null, isProtectedIdentity: false }]),
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
          JSON.stringify([{ kind: 0, conceptId: null, label: "Frontend", organizationNumber: null, adCount: null, isProtectedIdentity: false }]),
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
          JSON.stringify([{ kind: 0, conceptId: null, label: "AI-ingenjör", organizationNumber: null, adCount: null, isProtectedIdentity: false }]),
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

  // #757 — visible loading affordance. During an in-flight suggest fetch `items`
  // is empty, so `showList` is false and the suggestion list would unmount:
  // sighted users saw their suggestions flash out to an empty gap until the next
  // list popped in. A flat, aria-hidden `.jp-skeleton` row now holds the popup
  // surface during loading. The sr-only status region must stay the SOLE screen-
  // reader announcement, and the combobox must report `aria-expanded="false"`
  // during loading (the skeleton is not a list of selectable options).
  it("#757: shows an aria-hidden skeleton loading row during the in-flight fetch, then swaps to results", async () => {
    // A deferred fetch that stays pending until we resolve it — keeps the
    // component in `loading` long enough to observe the skeleton (the existing
    // suite's immediate responses would transition to `ready` too fast).
    let resolveFetch!: (r: Response) => void;
    const fetchMock = vi.fn(
      () =>
        new Promise<Response>((resolve) => {
          resolveFetch = resolve;
        }),
    );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    const { container } = render(<ControlledHarness onSelect={vi.fn()} />);
    await user.type(screen.getByRole("combobox"), "back");

    // Debounce fires → fetch is in flight → the skeleton row is present.
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1), {
      timeout: 2000,
    });
    await waitFor(() =>
      expect(container.querySelector(".jp-skeleton")).toBeInTheDocument(),
    );

    // sr-only region still announces loading (AC: unchanged) …
    expect(screen.getByRole("status")).toHaveTextContent("Hämtar förslag");
    // … the skeleton is decorative: it lives inside an aria-hidden subtree (the
    // popup wrapper carries aria-hidden, which hides its descendants from AT),
    // exposes no option role, and the combobox stays truthfully collapsed (no
    // list of options is displayed).
    expect(
      container.querySelector(".jp-skeleton")!.closest('[aria-hidden="true"]'),
    ).toBeInTheDocument();
    expect(screen.queryByRole("option")).not.toBeInTheDocument();
    expect(screen.getByRole("combobox")).toHaveAttribute(
      "aria-expanded",
      "false",
    );

    // Results land → the skeleton is replaced by option rows, and now the
    // combobox reports expanded.
    resolveFetch(
      new Response(
        JSON.stringify([
          { kind: 0, conceptId: null, label: "Backend-utvecklare", organizationNumber: null, adCount: null, isProtectedIdentity: false },
        ]),
        { status: 200 },
      ),
    );
    expect(
      await screen.findByRole(
        "option",
        { name: "Backend-utvecklare" },
        { timeout: 2000 },
      ),
    ).toBeInTheDocument();
    expect(container.querySelector(".jp-skeleton")).not.toBeInTheDocument();
    expect(screen.getByRole("combobox")).toHaveAttribute(
      "aria-expanded",
      "true",
    );
  });

  it("#757: no skeleton lingers once a rate-limited response lands", async () => {
    const fetchMock = vi.fn(async () => new Response("[]", { status: 429 }));
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    const { container } = render(<ControlledHarness onSelect={vi.fn()} />);
    await user.type(screen.getByRole("combobox"), "java");

    // The rate-limited degradation row shows (loading resolved to rateLimited)
    // and the skeleton is gone — the two popup states are mutually exclusive.
    await waitFor(
      () =>
        expect(
          screen.getByText(/För många sökningar på kort tid/),
        ).toBeInTheDocument(),
      { timeout: 2000 },
    );
    expect(container.querySelector(".jp-skeleton")).not.toBeInTheDocument();
  });

  it("#757: no skeleton lingers once results are empty (no popup)", async () => {
    const fetchMock = vi.fn(async () => new Response("[]", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    const { container } = render(<ControlledHarness onSelect={vi.fn()} />);
    await user.type(screen.getByRole("combobox"), "zzz");

    // Empty result set → `ready` with 0 items → no list, no skeleton, and the
    // sr-only region announces nothing (its empty-string branch).
    await waitFor(() => expect(fetchMock).toHaveBeenCalled(), { timeout: 2000 });
    await waitFor(() =>
      expect(container.querySelector(".jp-skeleton")).not.toBeInTheDocument(),
    );
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    expect(screen.getByRole("status").textContent).toBe("");
  });
});
