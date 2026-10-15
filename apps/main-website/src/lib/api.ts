import type { Category, ListingDetail, Locality, NearbyListing } from "./types";

// Server-side only: the browser never calls the API directly (architecture principle 1).
// API_BASE_URL is read at runtime, so one build works in staging and production.
const API_BASE_URL = process.env.API_BASE_URL ?? "http://localhost:5080";

async function getJson<T>(path: string, revalidateSeconds: number): Promise<T | null> {
  const response = await fetch(`${API_BASE_URL}${path}`, { next: { revalidate: revalidateSeconds } });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`API request ${path} failed with status ${response.status}`);
  return (await response.json()) as T;
}

export async function getCategories(): Promise<Category[]> {
  return (await getJson<Category[]>("/api/v1/public/categories", 300)) ?? [];
}

/** Returns null when the city does not exist. */
export async function getLocalities(citySlug: string): Promise<Locality[] | null> {
  return getJson<Locality[]>(`/api/v1/public/cities/${encodeURIComponent(citySlug)}/localities`, 300);
}

export async function getNearby(options: {
  lat: number;
  lng: number;
  category?: string;
  radiusKm?: number;
  limit?: number;
}): Promise<NearbyListing[]> {
  const query = new URLSearchParams({ lat: String(options.lat), lng: String(options.lng) });
  if (options.category) query.set("category", options.category);
  if (options.radiusKm) query.set("radiusKm", String(options.radiusKm));
  if (options.limit) query.set("limit", String(options.limit));
  return (await getJson<NearbyListing[]>(`/api/v1/public/listings/nearby?${query.toString()}`, 60)) ?? [];
}

export async function getListing(citySlug: string, localitySlug: string, slug: string): Promise<ListingDetail | null> {
  const path = [citySlug, localitySlug, slug].map(encodeURIComponent).join("/");
  return getJson<ListingDetail>(`/api/v1/public/listings/${path}`, 60);
}
