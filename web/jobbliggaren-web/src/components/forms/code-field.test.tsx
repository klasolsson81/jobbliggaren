import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderToStaticMarkup } from "react-dom/server";
import { CodeField } from "./code-field";

const LABEL = "Sexsiffrig kod";
const HINT = "Kommer inget mejl inom några minuter kan du skicka en ny kod.";

function renderField(invalid: boolean, onValueChange?: (value: string) => void) {
  return render(
    <>
      <CodeField
        id="reauth-code"
        hintId="reauth-code-hint"
        label={LABEL}
        hint={HINT}
        invalid={invalid}
        errorId="reauth-code-error"
        onValueChange={onValueChange}
      />
      <p id="reauth-code-error">Koden stämmer inte.</p>
    </>
  );
}

function slots(container: HTMLElement) {
  return Array.from(container.querySelectorAll('[data-slot="input-otp-slot"]'));
}

describe("CodeField", () => {
  it("is ONE one-time-code input with a visible label and no placeholder", () => {
    renderField(false);

    expect(screen.getAllByRole("textbox")).toHaveLength(1);
    const field = screen.getByLabelText(LABEL);
    expect(field.tagName).toBe("INPUT");
    expect(field).toHaveAttribute("autocomplete", "one-time-code");
    expect(field).toHaveAttribute("inputmode", "numeric");
    expect(field).toHaveAttribute("maxlength", "6");
    expect(field).toHaveAttribute("pattern", "^\\d+$");
    expect(field).toBeRequired();
    expect(field).not.toHaveAttribute("placeholder");
    // Class contract (see the size test below): forced colours would repaint its transparent text.
    expect(field).toHaveClass("forced-color-adjust-none");
  });

  it("draws six boxes that screen readers skip, showing the digits the input holds", async () => {
    const user = userEvent.setup();
    const { container } = renderField(false);

    expect(slots(container)).toHaveLength(6);
    expect(container.querySelector('[data-slot="input-otp-group"]')).toHaveAttribute(
      "aria-hidden",
      "true"
    );

    await user.type(screen.getByLabelText(LABEL), "123456");

    expect(screen.getByLabelText(LABEL)).toHaveValue("123456");
    expect(slots(container).map((slot) => slot.textContent)).toEqual(["1", "2", "3", "4", "5", "6"]);
  });

  // jsdom has no cascade: this pins the class contract, and the 20px was measured in a browser
  // (the same division as `ui/dialog.textrank.test.tsx`, #1601). `cn` reads `text-h2` as a colour
  // and drops it beside `text-text-primary`, so the size has to sit in the font-size group.
  it("sizes the digits in the font-size group, where the colour class cannot drop it", () => {
    const { container } = renderField(false);

    for (const slot of slots(container)) {
      expect(slot.getAttribute("class")).toMatch(/text-\(length:--text-h2\)/);
    }
  });

  it("draws the field for a browser without JavaScript in the tokens, the sans face and the focus ring", () => {
    const html = renderToStaticMarkup(
      <CodeField id="c" hintId="c-hint" label={LABEL} hint={HINT} invalid={false} errorId="c-error" />
    );
    const noscript = html.slice(html.indexOf("<noscript>"), html.indexOf("</noscript>"));

    expect(noscript).toContain("var(--jp-surface-primary)");
    expect(noscript).toContain("var(--jp-font-sans)");
    expect(noscript).toContain("[data-input-otp]:focus-visible{outline:2px solid var(--jp-focus)");
    expect(noscript).toContain("forced-color-adjust:auto!important");
    expect(noscript).toContain("[data-input-otp][aria-invalid=true]{border-color:var(--jp-danger-600)!important}");
    expect(noscript).not.toContain("prefers-color-scheme");
  });

  it("takes digits only", async () => {
    const user = userEvent.setup();
    renderField(false);

    await user.type(screen.getByLabelText(LABEL), "12a3");

    expect(screen.getByLabelText(LABEL)).toHaveValue("123");
  });

  it("takes a pasted code with the whitespace a mail can carry", async () => {
    const user = userEvent.setup();
    renderField(false);

    await user.click(screen.getByLabelText(LABEL));
    await user.paste("123 456\n");

    expect(screen.getByLabelText(LABEL)).toHaveValue("123456");
  });

  it("hands the caller the value as a string", async () => {
    const user = userEvent.setup();
    const onValueChange = vi.fn<(value: string) => void>();
    renderField(false, onValueChange);

    await user.type(screen.getByLabelText(LABEL), "42");

    expect(onValueChange).toHaveBeenLastCalledWith("42");
  });

  it("is described by its hint alone while there is nothing to correct", () => {
    renderField(false);

    const field = screen.getByLabelText(LABEL);
    expect(field).not.toHaveAttribute("aria-invalid");
    expect(field).toHaveAccessibleDescription(HINT);
  });

  it("is described by its hint AND the message once it is invalid", () => {
    renderField(true);

    const field = screen.getByLabelText(LABEL);
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field.getAttribute("aria-describedby")).toBe("reauth-code-hint reauth-code-error");
  });

  it("marks every box while the field is invalid, and none while it is not", () => {
    const { container, rerender } = renderField(false);
    expect(slots(container).filter((slot) => slot.classList.contains("border-destructive"))).toHaveLength(0);

    rerender(
      <>
        <CodeField
          id="reauth-code"
          hintId="reauth-code-hint"
          label={LABEL}
          hint={HINT}
          invalid
          errorId="reauth-code-error"
        />
        <p id="reauth-code-error">Koden stämmer inte.</p>
      </>
    );
    expect(slots(container).filter((slot) => slot.classList.contains("border-destructive"))).toHaveLength(6);
  });

  it("takes its ids from the caller, so two fields on one page never share one", () => {
    render(
      <>
        <CodeField id="a" hintId="a-hint" label="A" hint="a" invalid={false} errorId="a-error" />
        <CodeField id="b" hintId="b-hint" label="B" hint="b" invalid={false} errorId="b-error" />
      </>
    );

    expect(screen.getByLabelText("A")).toHaveAttribute("id", "a");
    expect(screen.getByLabelText("B")).toHaveAttribute("id", "b");
  });
});
