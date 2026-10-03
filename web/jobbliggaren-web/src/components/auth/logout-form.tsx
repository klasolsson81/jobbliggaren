import type { ReactNode } from "react";
import { LOGOUT_PATH } from "@/lib/auth/login-paths";

/**
 * "Logga ut" as a native POST to the logout route handler, never a Server Action, so the click runs on
 * whichever build answers it (#1956). Without `method="post"` the form would be a GET, which the route
 * does not answer.
 */
export function LogoutForm({ className, children }: { className?: string; children: ReactNode }) {
  return (
    <form action={LOGOUT_PATH} method="post" className={className}>
      {children}
    </form>
  );
}
