import { afterEach, describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { useFocusMainOnChange } from "./use-focus-main-on-change";

function Probe({ value }: { value: number }) {
  useFocusMainOnChange(value);
  return null;
}

// The activator keeps focus after a client-side navigation: the input stands in for the pager link that was pressed.
function Page({ value }: { value: number }) {
  return (
    <>
      <main id="main" tabIndex={-1}>
        <Probe value={value} />
      </main>
      <input aria-label="activator" />
    </>
  );
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("useFocusMainOnChange (design-reviewer Blocker 1, PR #2180)", () => {
  it("leaves focus alone on the first render", () => {
    const { getByLabelText } = render(<Page value={5} />);

    expect(document.activeElement).toBe(document.body);
    getByLabelText("activator").focus();
    expect(document.activeElement).toBe(getByLabelText("activator"));
  });

  it("leaves focus alone when the key does not change", () => {
    const { rerender, getByLabelText } = render(<Page value={5} />);
    const activator = getByLabelText("activator");
    activator.focus();

    rerender(<Page value={5} />);

    expect(document.activeElement).toBe(activator);
  });

  it("moves focus to #main without scrolling when the key changes", () => {
    const { rerender, getByLabelText } = render(<Page value={5} />);
    getByLabelText("activator").focus();
    const main = document.getElementById("main")!;
    const focus = vi.spyOn(main, "focus");

    rerender(<Page value={6} />);

    expect(focus).toHaveBeenCalledWith({ preventScroll: true });
    expect(document.activeElement).toBe(main);
  });
});
