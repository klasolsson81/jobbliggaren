// Re-export från lib/dto/me.ts. Zod-schemat är single source of truth
// per ADR 0020. Den här filen behålls för bakåtkompatibla konsument-
// importer (`@/lib/types/me`). Nya konsumenter bör importera från
// `@/lib/dto/me` direkt.
export type { JobSeekerProfileDto } from "@/lib/dto/me";
