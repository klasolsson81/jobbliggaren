import { AdminOverview } from "@/components/admin/admin-overview";
import type { AdminOverviewSnapshot } from "@/lib/dto/admin-overview";

const LOADING: AdminOverviewSnapshot = {
  accounts: { kind: "loading" }, audit: { kind: "loading" }, jobs: { kind: "loading" }, backup: { kind: "loading" },
};

export default function AdminOverviewLoading() {
  return <AdminOverview observations={LOADING} now={0} />;
}