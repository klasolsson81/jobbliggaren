import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CodeField } from "./code-field";

const LABEL = "Sexsiffrig kod";
const HINT = "Kommer inget mejl inom några minuter kan du skicka en ny kod.";

function renderField(invalid: boolean) {
  return render(
    <>
      <CodeField
        id="reauth-code"
        hintId="reauth-code-hint"
        label={LABEL}
        hint={HINT}
        invalid={invalid}
        errorId="reauth-code-error"
      />
      <p id="reauth-code-error">Koden stämmer inte.</p>
    </>
  );
}

describe("CodeField", () => {
  it("is ONE one-time-code field with a visible label and no placeholder, never six boxes", () => {
    renderField(false);

    expect(screen.getAllByRole("textbox")).toHaveLength(1);
    const field = screen.getByLabelText(LABEL);
    expect(field).toHaveAttribute("autocomplete", "one-time-code");
    expect(field).toHaveAttribute("inputmode", "numeric");
    expect(field).toHaveAttribute("maxlength", "6");
    expect(field).toHaveAttribute("pattern", "[0-9]*");
    expect(field).toBeRequired();
    expect(field).not.toHaveAttribute("placeholder");
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
