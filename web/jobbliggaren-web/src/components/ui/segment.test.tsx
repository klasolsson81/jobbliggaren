import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { Segment } from "./segment";

const themeOptions = [
  { value: "light" as const, label: "Ljust" },
  { value: "dark" as const, label: "Mörkt" },
];

describe("Segment — radiogroup-primitiv (F6 Prompt 2)", () => {
  it("renderar radiogroup-roll + alla options som radio-knappar", () => {
    render(
      <Segment
        aria-label="Tema"
        value="light"
        onChange={() => {}}
        options={themeOptions}
      />,
    );
    expect(screen.getByRole("radiogroup", { name: "Tema" })).toBeInTheDocument();
    const radios = screen.getAllByRole("radio");
    expect(radios).toHaveLength(2);
  });

  it("markerar aktiv option med aria-checked=true + data-active", () => {
    render(
      <Segment
        aria-label="Tema"
        value="dark"
        onChange={() => {}}
        options={themeOptions}
      />,
    );
    const ljust = screen.getByRole("radio", { name: "Ljust" });
    const mörkt = screen.getByRole("radio", { name: "Mörkt" });
    expect(ljust).toHaveAttribute("aria-checked", "false");
    expect(mörkt).toHaveAttribute("aria-checked", "true");
    expect(mörkt).toHaveAttribute("data-active", "true");
  });

  it("klick på option triggar onChange med dess value", () => {
    const onChange = vi.fn();
    render(
      <Segment
        aria-label="Tema"
        value="light"
        onChange={onChange}
        options={themeOptions}
      />,
    );
    fireEvent.click(screen.getByRole("radio", { name: "Mörkt" }));
    expect(onChange).toHaveBeenCalledWith("dark");
  });

  it("disabled option triggar inte onChange vid klick", () => {
    const onChange = vi.fn();
    render(
      <Segment
        aria-label="Språk"
        value="sv"
        onChange={onChange}
        options={[
          { value: "sv", label: "Svenska" },
          { value: "en", label: "English", disabled: true },
        ]}
      />,
    );
    const en = screen.getByRole("radio", { name: "English" });
    expect(en).toBeDisabled();
    fireEvent.click(en);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("disabled=true på hela gruppen stänger av alla options", () => {
    const onChange = vi.fn();
    render(
      <Segment
        aria-label="Tema"
        value="light"
        onChange={onChange}
        options={themeOptions}
        disabled
      />,
    );
    for (const radio of screen.getAllByRole("radio")) {
      expect(radio).toBeDisabled();
    }
    fireEvent.click(screen.getByRole("radio", { name: "Mörkt" }));
    expect(onChange).not.toHaveBeenCalled();
  });

  it("Höger-pil flyttar till nästa enabled option", () => {
    const onChange = vi.fn();
    render(
      <Segment
        aria-label="Tema"
        value="light"
        onChange={onChange}
        options={themeOptions}
      />,
    );
    const group = screen.getByRole("radiogroup");
    fireEvent.keyDown(group, { key: "ArrowRight" });
    expect(onChange).toHaveBeenCalledWith("dark");
  });

  it("Vänster-pil wrappar runt till sista option", () => {
    const onChange = vi.fn();
    render(
      <Segment
        aria-label="Tema"
        value="light"
        onChange={onChange}
        options={themeOptions}
      />,
    );
    const group = screen.getByRole("radiogroup");
    fireEvent.keyDown(group, { key: "ArrowLeft" });
    expect(onChange).toHaveBeenCalledWith("dark");
  });

  it("hoppar över disabled options vid piltangent-nav", () => {
    const onChange = vi.fn();
    render(
      <Segment
        aria-label="Språk"
        value="sv"
        onChange={onChange}
        options={[
          { value: "sv", label: "Svenska" },
          { value: "en", label: "English", disabled: true },
        ]}
      />,
    );
    const group = screen.getByRole("radiogroup");
    fireEvent.keyDown(group, { key: "ArrowRight" });
    // Bara "sv" är enabled → wrappar tillbaka till "sv" (samma value)
    expect(onChange).toHaveBeenCalledWith("sv");
  });
});
