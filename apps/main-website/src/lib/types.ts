// Shapes returned by the API (camelCase JSON). Keep in sync with backend/BusinessDirectory.Api/Endpoints.
// Later these come from the generated OpenAPI client in packages/api-client.

export type SubCategory = { slug: string; name: string; searchTerms: string[] };
export type Category = { slug: string; name: string; children: SubCategory[] };
export type Locality = { slug: string; name: string; latitude: number; longitude: number };

export type NearbyListing = {
  slug: string;
  name: string;
  citySlug: string;
  localitySlug: string;
  localityName: string;
  categorySlug: string;
  categoryName: string;
  distanceMetres: number;
  addressLine: string | null;
  phonePrimary: string;
  phoneWhatsapp: string | null;
  url: string;
};

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
