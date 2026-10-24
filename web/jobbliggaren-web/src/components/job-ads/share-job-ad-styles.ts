import { TEXT_LINK } from "@/components/auth/mail-link";

/**
 * One look for every item on the share row (ADR 0183): the house text link, semibold, with the 32 px target floor that
 * grows to 44 px at ≤768 px. Its own module because the server-rendered links and the client island both use it.
 */
export const SHARE_ITEM = `${TEXT_LINK} inline-flex min-h-8 min-w-8 items-center justify-center text-body-sm font-semibold [@media(max-width:768px)]:min-h-11 [@media(max-width:768px)]:min-w-11`;

/** A button that reads as the row's links, down to the hover tone the global link rule gives an anchor. */
export const SHARE_BUTTON = `${SHARE_ITEM} cursor-pointer border-0 bg-transparent p-0 hover:text-[var(--jp-accent-600)]`;
