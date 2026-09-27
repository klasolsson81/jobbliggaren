import { MinaSidorLoading } from "@/components/settings/mina-sidor-shell";

/** Route-level loading state for /mina-sidor, its Matchning section (#739, #1891). */
export default function Loading() {
  return <MinaSidorLoading active="matchning" />;
}
