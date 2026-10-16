// Shapes returned by the API (camelCase JSON). Keep in sync with backend/BusinessDirectory.Api/Endpoints.
// Later these come from the generated OpenAPI client in packages/api-client.

export type SubCategory = { slug: string; name: string; searchTerms: string[] };
export type Category = { slug: string; name: string; children: SubCategory[] };
export type Locality = { slug: string; name: string; latitude: number; longitude: number };

export type ListingDetail = {
  slug: string;
  name: string;
  description: string | null;
  addressLine: string | null;
  pincode: string | null;
  phonePrimary: string;
  phoneWhatsapp: string | null;
  websiteUrl: string | null;
  latitude: number;
  longitude: number;
  citySlug: string;
  cityName: string;
  localitySlug: string;
  localityName: string;
  categorySlug: string;
  categoryName: string;
  url: string;
};

/** Flat, search-friendly view of the category tree (sub-categories first, then parents). */
export type SearchCategory = { slug: string; name: string; terms: string[] };

/** One row from /api/v1/public/listings/search. distanceMetres is null unless lat/lng were sent. */
export type SearchListing = {
  slug: string;
  name: string;
  citySlug: string;
  localitySlug: string;
  localityName: string;
  categorySlug: string;
  categoryName: string;
  distanceMetres: number | null;
  addressLine: string | null;
  phonePrimary: string;
  phoneWhatsapp: string | null;
  url: string;
};

/** A page of search results. `total` counts every match, not just this page. */
export type SearchResults = {
  items: SearchListing[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
};

/** What the visitor asked for. Mirrors the query string so filters stay shareable. */
export type SearchCriteria = {
  q?: string;
  city?: string;
  locality?: string;
  category?: string;
  lat?: number;
  lng?: number;
  radiusKm?: number;
  sort?: "relevance" | "distance" | "name";
  page?: number;
  pageSize?: number;
};
