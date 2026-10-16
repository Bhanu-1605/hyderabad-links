import type { Metadata } from "next";
import { ListingCard } from "@/components/ListingCard";
import { Pagination } from "@/components/Pagination";
import { SearchFilters } from "@/components/SearchFilters";
import { getCategories, getLocalities, searchListings } from "@/lib/api";
import { DEFAULT_CITY, DEFAULT_CITY_NAME } from "@/lib/brand";
import { findCategory } from "@/lib/categories";
import type { SearchCriteria, SearchResults } from "@/lib/types";

export const metadata: Metadata = {
  title: "Search",
  robots: { index: false, follow: true }, // filtered result pages are not for search engines
};

type RawParams = {
  q?: string;
  category?: string;
  locality?: string;
  sort?: string;
  lat?: string;
  lng?: string;
  radiusKm?: string;
  page?: string;
};

type Props = { searchParams: Promise<RawParams> };

/** Only values the API accepts reach it; anything else is dropped so a stray URL cannot 400 the page. */
function toCriteria(raw: RawParams): SearchCriteria {
  const lat = Number(raw.lat);
  const lng = Number(raw.lng);
  const hasCoordinates = raw.lat !== undefined && raw.lng !== undefined && Number.isFinite(lat) && Number.isFinite(lng);
  const radiusKm = Number(raw.radiusKm);
  const page = Number(raw.page);

  return {
    q: raw.q?.trim() || undefined,
    city: DEFAULT_CITY,
    category: raw.category || undefined,
    locality: raw.locality || undefined,
    lat: hasCoordinates ? lat : undefined,
    lng: hasCoordinates ? lng : undefined,
    radiusKm: hasCoordinates && Number.isFinite(radiusKm) ? radiusKm : undefined,
    sort:
      raw.sort === "distance" && hasCoordinates ? "distance"
      : raw.sort === "name" ? "name"
      : raw.sort === "relevance" ? "relevance"
      : undefined,
    page: Number.isFinite(page) && page > 0 ? Math.floor(page) : undefined,
  };
}

function headingFor(criteria: SearchCriteria, categoryName: string | null, localityName: string | null): string {
  const what = criteria.q ? `"${criteria.q}"` : categoryName ?? "Businesses";
  const where = localityName ?? (criteria.lat !== undefined ? "near you" : DEFAULT_CITY_NAME);
  return `${what} in ${where}`.replace(" in near you", " near you");
}

export default async function SearchPage({ searchParams }: Props) {
  const raw = await searchParams;
  const criteria = toCriteria(raw);

  const [tree, localities] = await Promise.all([getCategories(), getLocalities(DEFAULT_CITY)]);
  const areas = localities ?? [];

  let results: SearchResults | null = null;
  let failed = false;
  try {
    results = await searchListings(criteria);
  } catch {
    // The API is down or rejected the request. Show the filters and a retry message rather than a crash page.
    failed = true;
  }

  const categoryName = criteria.category ? findCategory(tree, criteria.category)?.name ?? null : null;
  const localityName = criteria.locality ? areas.find((a) => a.slug === criteria.locality)?.name ?? null : null;

  return (
    <main className="mx-auto max-w-5xl px-4 pt-8">
      <SearchFilters criteria={criteria} categories={tree} localities={areas} />

      <h1 className="mt-8 text-2xl font-bold">{headingFor(criteria, categoryName, localityName)}</h1>

      {failed ? (
        <div role="alert" className="mt-4 rounded-xl border border-red-200 bg-red-50 p-4">
          <p className="font-medium text-red-900">We could not load results just now.</p>
          <p className="mt-1 text-sm text-red-800">Please try the search again in a moment.</p>
        </div>
      ) : results && results.total === 0 ? (
        <div className="mt-4 rounded-xl border border-neutral-200 bg-white p-6">
          <p className="font-medium">No businesses matched this search.</p>
          <p className="mt-1 text-sm text-neutral-600">
            Try a different keyword, pick a wider category, or choose &ldquo;All areas&rdquo;.
          </p>
        </div>
      ) : results ? (
        <>
          <p className="mt-1 text-sm text-neutral-500">
            {results.total} {results.total === 1 ? "business" : "businesses"} found
            {criteria.sort === "distance" ? ", closest first" : ""}
          </p>

          {results.items.length === 0 ? (
            // A page number past the end: the total above still tells the visitor what exists.
            <p className="mt-4 text-neutral-600">
              This page is empty. Go back to <a href="/search?page=1" className="text-brand-700 underline">the first page</a>.
            </p>
          ) : (
            <ul className="mt-4 grid gap-3">
              {results.items.map((listing) => (
                <ListingCard key={listing.url} listing={listing} />
              ))}
            </ul>
          )}

          <Pagination
            page={results.page}
            totalPages={results.totalPages}
            params={raw as Record<string, string | undefined>}
            basePath="/search"
          />
        </>
      ) : null}
    </main>
  );
}
