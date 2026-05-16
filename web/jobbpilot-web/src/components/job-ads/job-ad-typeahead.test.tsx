import { describe, it, expect, vi, afterEach } from "vitest";
import { useState } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobAdTypeahead } from "./job-ad-typeahead";

function ControlledHarness({
  onSelect,
}: {
  onSelect: (t: string) => void;
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
 * Real timers (ingen fake) — debouncen är 300ms; waitFor med generös
 * timeout täcker det utan fake-timer/userEvent-deadlock. Stabilare än att
 * driva fake-timers manuellt genom userEvent + microtask-fetch-kedjan.
 */
describe("JobAdTypeahead (ADR 0042 Beslut C)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("does not call the suggest endpoint for prefixes under 2 chars", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response("[]", { status: 200 })
    );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    render(<ControlledHarness onSelect={vi.fn()} />);
    await user.type(screen.getByRole("combobox"), "a");

    // Vänta förbi debounce-fönstret + marginal — ingen request ska ha skett.
    await new Promise((r) => setTimeout(r, 500));
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("debounces then fetches suggestions and renders them", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(JSON.stringify(["Backend-utvecklare"]), {
          status: 200,
        })
    );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    render(<ControlledHarness onSelect={vi.fn()} />);
    await user.type(screen.getByRole("combobox"), "back");

    expect(
      await screen.findByRole(
        "button",
        { name: "Backend-utvecklare" },
        { timeout: 2000 }
      )
    ).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/api/jobb/suggest?prefix=back"),
      expect.anything()
    );
  });

  it("selecting a suggestion calls onSelect with the term", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(JSON.stringify(["Frontend-utvecklare"]), {
          status: 200,
        })
    );
    vi.stubGlobal("fetch", fetchMock);
    const onSelect = vi.fn();
    const user = userEvent.setup();

    render(<ControlledHarness onSelect={onSelect} />);
    await user.type(screen.getByRole("combobox"), "front");

    const option = await screen.findByRole(
      "button",
      { name: "Frontend-utvecklare" },
      { timeout: 2000 }
    );
    await user.click(option);

    expect(onSelect).toHaveBeenCalledWith("Frontend-utvecklare");
  });

  it("handles a 429 rateLimited response civilly", async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response("[]", { status: 429 })
    );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    render(<ControlledHarness onSelect={vi.fn()} />);
    await user.type(screen.getByRole("combobox"), "java");

    await waitFor(
      () =>
        expect(
          screen.getByText(/För många sökningar på kort tid/)
        ).toBeInTheDocument(),
      { timeout: 2000 }
    );
  });
});
