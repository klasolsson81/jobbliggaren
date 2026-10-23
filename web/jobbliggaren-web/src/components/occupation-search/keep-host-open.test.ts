import { afterEach, describe, expect, it } from "vitest";
import {
  OCCUPATION_SEARCH_FIELD_ATTRIBUTE,
  keepHostOpenWhileSearchHasText,
} from "./keep-host-open";

function escapeOn(target: HTMLElement): KeyboardEvent {
  const event = new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true });
  target.dispatchEvent(event);
  return event;
}

function field(value: string, marked = true): HTMLInputElement {
  const input = document.createElement("input");
  input.type = "search";
  if (marked) input.setAttribute(OCCUPATION_SEARCH_FIELD_ATTRIBUTE, "");
  input.value = value;
  document.body.append(input);
  return input;
}

const listener = (event: KeyboardEvent) => keepHostOpenWhileSearchHasText(event);
afterEach(() => {
  document.removeEventListener("keydown", listener, { capture: true });
  document.body.replaceChildren();
});

describe("keepHostOpenWhileSearchHasText (#2198)", () => {
  // Registered as Radix registers its own Escape listener: on the document, in the capture phase.
  it("holds the host open only while the marked search field has text", () => {
    document.addEventListener("keydown", listener, { capture: true });

    expect(escapeOn(field("projektledare")).defaultPrevented).toBe(true);
    expect(escapeOn(field("")).defaultPrevented).toBe(false);
    expect(escapeOn(field("projektledare", false)).defaultPrevented).toBe(false);
    expect(escapeOn(document.body).defaultPrevented).toBe(false);
  });

  it("runs before the field's own handler, so it still sees the text the field is about to clear", () => {
    const input = field("chef");
    let seenByHost: string | null = null;
    document.addEventListener(
      "keydown",
      (event) => {
        seenByHost = (event.target as HTMLInputElement).value;
        keepHostOpenWhileSearchHasText(event);
      },
      { capture: true, once: true },
    );
    input.addEventListener("keydown", () => {
      input.value = "";
    });

    const event = escapeOn(input);

    expect(seenByHost).toBe("chef");
    expect(input.value).toBe("");
    expect(event.defaultPrevented).toBe(true);
  });
});
