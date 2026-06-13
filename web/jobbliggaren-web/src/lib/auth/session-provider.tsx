"use client";

import { createContext, useContext, useEffect, useState } from "react";
import type { CurrentUser } from "@/lib/auth/session";

type SessionContextValue = {
  user: CurrentUser | null;
  loading: boolean;
};

const SessionContext = createContext<SessionContextValue>({
  user: null,
  loading: true,
});

export function SessionProvider({
  children,
  initialUser = null,
}: {
  children: React.ReactNode;
  initialUser?: CurrentUser | null;
}) {
  const [user, setUser] = useState<CurrentUser | null>(initialUser);
  const [loading, setLoading] = useState(initialUser === null);

  useEffect(() => {
    if (initialUser !== null) return;
    fetch("/api/me")
      .then((r) => (r.ok ? (r.json() as Promise<CurrentUser>) : null))
      .then((u) => {
        setUser(u);
        setLoading(false);
      })
      .catch(() => setLoading(false));
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <SessionContext.Provider value={{ user, loading }}>
      {children}
    </SessionContext.Provider>
  );
}

export function useSession(): SessionContextValue {
  return useContext(SessionContext);
}
