import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { LOGOUT_PATH } from "@/lib/auth/login-paths";
import { LogoutForm } from "./logout-form";

describe("LogoutForm (#1956)", () => {
  it("posts natively to the logout route, so the click does not depend on the build that rendered the page", () => {
    render(
      <LogoutForm className="foot">
        <button type="submit">Logga ut</button>
      </LogoutForm>
    );

    const form = screen.getByRole("button", { name: "Logga ut" }).closest("form");
    expect(form).not.toBeNull();
    expect(form).toHaveAttribute("action", LOGOUT_PATH);
    // A form without it is a GET, which the route does not answer.
    expect(form).toHaveAttribute("method", "post");
    expect(form).toHaveClass("foot");
  });
});
