import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyFollowButton } from "./company-follow-button";

const followActionMock = vi.fn();
const unfollowActionMock = vi.fn();

vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyAction: (...args: unknown[]) => followActionMock(...args),
  unfollowCompanyAction: (...args: unknown[]) => unfollowActionMock(...args),
}));

beforeEach(() => {
  followActionMock.mockReset();
  unfollowActionMock.mockReset();
});

const ORG_NR = "5592804784";
const COMPANY = "Acme Bygg AB";

describe("CompanyFollowButton", () => {
  it("renders 'Bevaka' with a company-specific accessible name when not following", () => {
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId={null}
      />
    );
    expect(
      screen.getByRole("button", { name: `Bevaka ${COMPANY}` })
    ).toBeInTheDocument();
    expect(screen.getByText("Bevaka")).toBeInTheDocument();
  });

  it("renders 'Bevakar' with a company-specific accessible name when already following", () => {
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId="cw1"
      />
    );
    expect(
      screen.getByRole("button", { name: `Bevakar ${COMPANY}` })
    ).toBeInTheDocument();
    expect(screen.getByText("Bevakar")).toBeInTheDocument();
  });

  it("keeps the visible label word inside the accessible name in both states (WCAG 2.5.3)", () => {
    // Fresh mounts, not rerender: `following` seeds from the prop via useState (mount-only), so a
    // rerender with a new prop would not flip state.
    const { unmount } = render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId={null}
      />
    );
    // Not following: visible "Bevaka" is contained in the accessible name "Bevaka {company}".
    expect(screen.getByRole("button")).toHaveAccessibleName(`Bevaka ${COMPANY}`);
    unmount();
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId="cw1"
      />
    );
    // Following: visible "Bevakar" is contained in "Bevakar {company}" — never "Sluta bevaka …".
    expect(screen.getByRole("button")).toHaveAccessibleName(`Bevakar ${COMPANY}`);
  });

  it("calls followCompanyAction with the org.nr and flips to following", async () => {
    followActionMock.mockResolvedValue({ success: true, companyWatchId: "cw-new" });
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId={null}
      />
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: `Bevaka ${COMPANY}` }));

    expect(followActionMock).toHaveBeenCalledWith(ORG_NR);
    expect(await screen.findByText("Bevakar")).toBeInTheDocument();
  });

  it("calls unfollowCompanyAction with the CompanyWatchId when following", async () => {
    unfollowActionMock.mockResolvedValue({ success: true });
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId="cw1"
      />
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: `Bevakar ${COMPANY}` }));

    expect(unfollowActionMock).toHaveBeenCalledWith("cw1");
    expect(await screen.findByText("Bevaka")).toBeInTheDocument();
  });

  it("rolls back to 'Bevaka' when follow fails", async () => {
    followActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte bevaka företaget. Försök igen.",
    });
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId={null}
      />
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: `Bevaka ${COMPANY}` }));

    expect(
      await screen.findByText(/Kunde inte bevaka företaget/i)
    ).toBeInTheDocument();
    expect(screen.getByText("Bevaka")).toBeInTheDocument();
  });

  /**
   * Geometry the browse table depends on (#1122). This button lives in a `table-layout: fixed` cell
   * that can no longer grow to fit its content, so two layout decisions here are load-bearing OVER
   * THERE and invisible to any assertion made in `company-browse-list.test.tsx` — both survived
   * mutation until this test existed.
   */
  it("lets the failure message wrap without dragging the button's width with it", async () => {
    followActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte bevaka företaget. Försök igen.",
    });
    const { container } = render(
      <CompanyFollowButton orgNr={ORG_NR} companyName={COMPANY} initialCompanyWatchId={null} />
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: `Bevaka ${COMPANY}` }));
    await screen.findByText(/Kunde inte bevaka företaget/i);

    // The button may not break: a label across two lines stops reading as a control. The cell around
    // it deliberately does NOT carry nowrap, so the button has to carry its own.
    expect(screen.getByRole("button")).toHaveClass("whitespace-nowrap");
    // One assertion for the whole box, because `alignItems` only means anything given the other
    // two: under the default `stretch` the ~230px sentence resizes the control the user is about
    // to retry, and inside the browse table's 160px fixed column it also paints across the edge.
    expect(container.firstElementChild).toHaveStyle({
      display: "inline-flex",
      flexDirection: "column",
      alignItems: "start",
    });
  });

  it("rolls back to 'Bevakar' when unfollow fails", async () => {
    unfollowActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte sluta bevaka företaget. Försök igen.",
    });
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId="cw1"
      />
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: `Bevakar ${COMPANY}` }));

    expect(
      await screen.findByText(/Kunde inte sluta bevaka företaget/i)
    ).toBeInTheDocument();
    expect(screen.getByText("Bevakar")).toBeInTheDocument();
  });

  it("uses the id from a successful follow when unfollowing next", async () => {
    followActionMock.mockResolvedValue({ success: true, companyWatchId: "cw-resolved" });
    unfollowActionMock.mockResolvedValue({ success: true });
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId={null}
      />
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: `Bevaka ${COMPANY}` }));
    await screen.findByText("Bevakar");

    await user.click(screen.getByRole("button", { name: `Bevakar ${COMPANY}` }));

    expect(unfollowActionMock).toHaveBeenCalledWith("cw-resolved");
    expect(await screen.findByText("Bevaka")).toBeInTheDocument();
  });
});
