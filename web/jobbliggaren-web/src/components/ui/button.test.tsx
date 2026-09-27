import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Button } from "./button";

const FLOOR_H = "[@media(max-width:768px)]:min-h-11";
const FLOOR_W = "[@media(max-width:768px)]:min-w-11";
const SIZES = ["default", "xs", "sm", "lg", "icon", "icon-xs", "icon-sm", "icon-lg"] as const;
const ICON_STEPS_UNDER_44 = ["icon", "icon-xs", "icon-sm"] as const;

const classesOf = (el: HTMLElement) => el.className.split(/\s+/);

describe("Button touch floor (DESIGN.md §5)", () => {
  it.each(SIZES)("size %s is at least 44 px tall at ≤768 px", (size) => {
    render(<Button size={size}>Spara</Button>);
    expect(classesOf(screen.getByRole("button"))).toContain(FLOOR_H);
  });

  it.each(ICON_STEPS_UNDER_44)("icon step %s is at least 44 px wide at ≤768 px", (size) => {
    render(<Button size={size} aria-label="Stäng" />);
    expect(classesOf(screen.getByRole("button"))).toContain(FLOOR_W);
  });

  it("keeps the floor when a consumer sets its own height and min-height", () => {
    render(<Button className="h-auto min-h-10 max-md:min-h-11">Fortsätt</Button>);
    expect(classesOf(screen.getByRole("button"))).toContain(FLOOR_H);
  });

  it("reaches the element a Button renders through asChild", () => {
    render(
      <Button asChild>
        <a href="/logga-in">Fortsätt</a>
      </Button>,
    );
    expect(classesOf(screen.getByRole("link"))).toContain(FLOOR_H);
  });
});
