import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { JsonLd } from "@/components/JsonLd";
import { ListingCard } from "@/components/ListingCard";
import { SearchBox } from "@/components/SearchBox";
import { getCategories, getLocalities, getNearby } from "@/lib/api";
import { DEFAULT_CITY, DEFAULT_CITY_NAME } from "@/lib/brand";
import { findCategory, toSearchCategories } from "@/lib/categories";
import { absoluteUrl } from "@/lib/format";

type Props = { params: Promise<{ city: string; locality: string; category: string }> };

async function loadPage(params: Props["params"]) {
  const { city, locality, category } = await params;
  if (city !== DEFAULT_CITY) return null;
  const [tree, localities] = await Promise.all([getCategories(), getLocalities(city)]);
  const area = localities?.find((l) => l.slug === locality);
  const found = findCategory(tree, category);
  if (!localities || !area || !found) return null;
  return { city, tree, localities, area, found };
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const page = await loadPage(params);
  if (!page) return { title: "Not found", robots: { index: false } };
  const title = `${page.found.name} in ${page.area.name}, ${DEFAULT_CITY_NAME}`;
  const listings = await getNearby({ lat: page.area.latitude, lng: page.area.longitude, category: page.found.slug, radiusKm: 8, limit: 30 });
  return {
    title,
    description: `Find ${page.found.name.toLowerCase()} in and near ${page.area.name}, ${DEFAULT_CITY_NAME}. Call or WhatsApp local businesses directly.`,
    alternates: { canonical: absoluteUrl(`/${page.city}/${page.area.slug}/${page.found.slug}`) },
    robots: listings.length === 0 ? { index: false, follow: true } : undefined, // avoid indexing empty pages
  };
}

export default async function CategoryInLocalityPage({ params }: Props) {
  const page = await loadPage(params);
  if (!page) notFound();

  const listings = await getNearby({ lat: page.area.latitude, lng: page.area.longitude, category: page.found.slug, radiusKm: 8, limit: 30 });

  return (
    <main className="mx-auto max-w-5xl px-4 pt-8">
      <SearchBox categories={toSearchCategories(page.tree)} localities={page.localities} initialQuery={page.found.name} />

      <h1 className="mt-8 text-2xl font-bold">
        {page.found.name} in {page.area.name}, {DEFAULT_CITY_NAME}
      </h1>
      <p className="mt-1 text-sm text-neutral-500">Closest first, including nearby areas.</p>

      {listings.length === 0 ? (
        <p className="mt-4 text-neutral-600">No listings here yet. Try a nearby area or another service.</p>
      ) : (
        <ul className="mt-4 grid gap-3">
          {listings.map((listing) => (
            <ListingCard key={listing.url} listing={listing} />
          ))}
        </ul>
      )}

      <JsonLd
        data={{
          "@context": "https://schema.org",
          "@type": "ItemList",
          name: `${page.found.name} in ${page.area.name}, ${DEFAULT_CITY_NAME}`,
          itemListElement: listings.map((listing, index) => ({
            "@type": "ListItem",
            position: index + 1,
            url: absoluteUrl(listing.url),
            name: listing.name,
          })),
        }}
      />
    </main>
  );
}
