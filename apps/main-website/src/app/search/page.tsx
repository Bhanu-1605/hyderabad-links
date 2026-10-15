import type { Metadata } from "next";
import { ListingCard } from "@/components/ListingCard";
import { SearchBox } from "@/components/SearchBox";
import { getCategories, getLocalities, getNearby } from "@/lib/api";
import { DEFAULT_CITY } from "@/lib/brand";
import { findCategory, toSearchCategories } from "@/lib/categories";

export const metadata: Metadata = {
  title: "Search",
  robots: { index: false, follow: true }, // location-specific results are not for search engines
};

type Props = { searchParams: Promise<{ category?: string; lat?: string; lng?: string }> };

export default async function SearchPage({ searchParams }: Props) {
  const { category, lat, lng } = await searchParams;
  const [tree, localities] = await Promise.all([getCategories(), getLocalities(DEFAULT_CITY)]);
  const found = category ? findCategory(tree, category) : null;

  const latitude = Number(lat);
  const longitude = Number(lng);
  const hasLocation = lat !== undefined && lng !== undefined && Number.isFinite(latitude) && Number.isFinite(longitude);
  const listings = hasLocation ? await getNearby({ lat: latitude, lng: longitude, category, radiusKm: 10, limit: 30 }) : [];

  return (
    <main className="mx-auto max-w-5xl px-4 pt-8">
      <SearchBox categories={toSearchCategories(tree)} localities={localities ?? []} initialQuery={found?.name ?? ""} />

      <h1 className="mt-8 text-2xl font-bold">{found ? `${found.name} near you` : "Businesses near you"}</h1>

      {!hasLocation ? (
        <p className="mt-3 text-neutral-600">Choose your area above, or use your current location, to see results.</p>
      ) : listings.length === 0 ? (
        <p className="mt-3 text-neutral-600">No listings found within 10 km yet. Try another service or area.</p>
      ) : (
        <ul className="mt-4 grid gap-3">
          {listings.map((listing) => (
            <ListingCard key={listing.url} listing={listing} />
          ))}
        </ul>
      )}
    </main>
  );
}
