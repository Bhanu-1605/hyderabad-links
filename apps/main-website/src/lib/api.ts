import type {
  Category,
  ListingDetail,
  Locality,
  LocalitySuggestion,
  NearestLocality,
  SearchCriteria,
  SearchResults,
} from "./types";

// Server-side only: the browser never calls the API directly (architecture principle 1).
// API_BASE_URL is read at runtime, so one build works in staging and production.
const API_BASE_URL = process.env.API_BASE_URL ?? "http://localhost:5080";

async function getJson<T>(path: string, revalidateSeconds: number): Promise<T | null> {
  const response = await fetch(`${API_BASE_URL}${path}`, { next: { revalidate: revalidateSeconds } });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`API request ${path} failed with status ${response.status}`);
  return (await response.json()) as T;
}

/**
 * The category tree, used by the site header on every page.
 *
 * This one swallows failures on purpose. The header renders everywhere, including the 404 page,
 * which Next.js prerenders at build time when no API is running (CI builds with no API at all).
 * A category list we cannot load should degrade to an empty menu, never take the whole page — or
 * the build — down with it. Pages that need categories to be meaningful check for an empty list.
 */
export async function getCategories(): Promise<Category[]> {
  try {
    return (await getJson<Category[]>("/api/v1/public/categories", 300)) ?? [];
  } catch {
    return [];
  }
}

/** Returns null when the city does not exist. */
export async function getLocalities(citySlug: string): Promise<Locality[] | null> {
  return getJson<Locality[]>(`/api/v1/public/cities/${encodeURIComponent(citySlug)}/localities`, 300);
}

/**
 * Partial-name locality lookup for the location autocomplete. Called by the route handler in
 * src/app/api/localities, never by the browser directly (architecture principle 1).
 *
 * The same fragment is typed by many visitors, so the answers are worth caching briefly; the
 * locality list itself changes only when the reference data does.
 */
export async function searchLocalities(text: string, city: string, limit = 8): Promise<LocalitySuggestion[]> {
  const query = new URLSearchParams({ q: text, city, limit: String(limit) });
  return (await getJson<LocalitySuggestion[]>(`/api/v1/public/localities/search?${query.toString()}`, 300)) ?? [];
}

/**
 * Resolves a visitor's coordinates to the nearest locality we know about, so "use my current
 * location" can show a real area name instead of raw numbers. Returns null when the API has no
 * active locality to offer.
 */
export async function getNearestLocality(lat: number, lng: number): Promise<NearestLocality | null> {
  const query = new URLSearchParams({ lat: String(lat), lng: String(lng) });
  // Every visitor's coordinates differ, so caching these would fill the cache with single-use entries.
  return getJson<NearestLocality>(`/api/v1/public/localities/nearest?${query.toString()}`, 0);
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
