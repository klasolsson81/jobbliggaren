"use client";
import { useContext, useLayoutEffect, useState } from "react";
import { InformationNavigationContext } from "./InformationReturnProvider";
import type { InformationSnapshots, SnapshotKey } from "./types";
// Route-owned state registers after commit so discarded renders cannot consume a restore.
export function useInformationAdapter<K extends SnapshotKey>(key: K, capture: () => InformationSnapshots[K]) {
  const navigation = useContext(InformationNavigationContext);
  useLayoutEffect(() => navigation?.register(key, capture), [navigation, key, capture]);
}
export function useInformationSnapshot<K extends SnapshotKey>(key: K) {
  const navigation = useContext(InformationNavigationContext);
  const [restored] = useState(() => navigation?.read(key));
  return restored;
}
