"use client";
import Link from "next/link";
import { useContext, type ComponentProps } from "react";
import { InformationNavigationContext } from "./InformationReturnProvider";
// Client click handling captures the source before navigation; href still works without JavaScript.
export function InformationLink({ href, onClick, ...props }: Omit<ComponentProps<typeof Link>, "href"> & { href: string }) {
  const navigation = useContext(InformationNavigationContext);
  return <Link {...props} href={href} data-information-link="" onClick={event => {
    onClick?.(event);
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || props.target === "_blank" || props.download) return;
    if (navigation?.navigate(href, event.currentTarget)) event.preventDefault();
  }} />;
}
