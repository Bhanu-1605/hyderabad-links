import type { Category, ListingDetail, Locality, SearchCriteria, SearchResults } from "./types";

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

/**
 * Database-backed listing search: keyword, category, locality, proximity, sorting and paging.
 * Throws when the API rejects the request or is unreachable, so the page can show an error state.
 */
export async function searchListings(criteria: SearchCriteria): Promise<SearchResults> {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(criteria)) {
    if (value !== undefined && value !== null && value !== "") {
      query.set(key, String(value));
    }
  }
  const results = await getJson<SearchResults>(`/api/v1/public/listings/search?${query.toString()}`, 60);
  // The endpoint always answers 200, so null would mean a shape change rather than "not found".
  return results ?? { items: [], page: 1, pageSize: 0, total: 0, totalPages: 0 };
}

export async function getListing(citySlug: string, localitySlug: string, slug: string): Promise<ListingDetail | null> {
  const path = [citySlug, localitySlug, slug].map(encodeURIComponent).join("/");
  return getJson<ListingDetail>(`/api/v1/public/listings/${path}`, 60);
}
