import type { Metadata } from "next";
import { notFound, permanentRedirect } from "next/navigation";
import Link from "next/link";
import { JsonLd } from "@/components/JsonLd";
import { getListing } from "@/lib/api";
import { absoluteUrl, mapsHref, telHref, whatsappHref } from "@/lib/format";

type Props = { params: Promise<{ city: string; locality: string; category: string; slug: string }> };

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { city, locality, slug } = await params;
  const listing = await getListing(city, locality, slug);
  if (!listing) return { title: "Not found", robots: { index: false } };
  return {
    title: `${listing.name} - ${listing.categoryName} in ${listing.localityName}, ${listing.cityName}`,
    description: listing.description ?? `${listing.name}, ${listing.categoryName} in ${listing.localityName}, ${listing.cityName}.`,
    alternates: { canonical: absoluteUrl(listing.url) },
  };
}

export default async function ListingPage({ params }: Props) {
  const { city, locality, category, slug } = await params;
  const listing = await getListing(city, locality, slug);
  if (!listing) notFound();

  // The category in the URL must be the listing's primary category, otherwise 301 to the canonical URL (decision D4).
  if (category !== listing.categorySlug) permanentRedirect(listing.url);

  return (
    <main className="mx-auto max-w-3xl px-4 pt-8">
      <nav className="text-sm text-neutral-500">
        <Link href={`/${listing.citySlug}/${listing.localitySlug}/${listing.categorySlug}`} className="hover:underline">
          {listing.categoryName} in {listing.localityName}
        </Link>
      </nav>

      <h1 className="mt-2 text-3xl font-bold tracking-tight">{listing.name}</h1>
      <p className="mt-1 text-neutral-600">
        {listing.categoryName} &middot; {listing.localityName}, {listing.cityName}
      </p>

      <div className="mt-5 flex flex-wrap gap-3">
        <a href={telHref(listing.phonePrimary)} className="rounded-lg bg-brand-700 px-5 py-2.5 font-medium text-white hover:bg-brand-900">
          Call {listing.phonePrimary}
        </a>
        {listing.phoneWhatsapp ? (
          <a
            href={whatsappHref(listing.phoneWhatsapp)}
            target="_blank"
            rel="noopener noreferrer"
            className="rounded-lg border border-neutral-300 bg-white px-5 py-2.5 font-medium hover:bg-neutral-50"
          >
            WhatsApp
          </a>
        ) : null}
        <a
          href={mapsHref(listing.latitude, listing.longitude)}
          target="_blank"
          rel="noopener noreferrer"
          className="rounded-lg border border-neutral-300 bg-white px-5 py-2.5 font-medium hover:bg-neutral-50"
        >
          Directions
        </a>
      </div>

      <section className="mt-8 rounded-xl border border-neutral-200 bg-white p-5">
        {listing.description ? <p className="text-neutral-700">{listing.description}</p> : null}
        {listing.addressLine ? (
          <p className="mt-3 text-neutral-600">
            <span className="font-medium text-neutral-800">Address: </span>
            {listing.addressLine}
            {listing.pincode ? ` - ${listing.pincode}` : ""}
          </p>
        ) : null}
        {listing.websiteUrl ? (
          <p className="mt-2">
            <a href={listing.websiteUrl} target="_blank" rel="noopener noreferrer nofollow" className="text-brand-700 hover:underline">
              Visit website
            </a>
          </p>
        ) : null}
      </section>

      <JsonLd
        data={{
          "@context": "https://schema.org",
          "@type": "LocalBusiness",
          name: listing.name,
          description: listing.description ?? undefined,
          url: absoluteUrl(listing.url),
          telephone: listing.phonePrimary,
          address: {
            "@type": "PostalAddress",
            streetAddress: listing.addressLine ?? undefined,
            addressLocality: listing.localityName,
            addressRegion: "Telangana",
            postalCode: listing.pincode ?? undefined,
            addressCountry: "IN",
          },
          geo: { "@type": "GeoCoordinates", latitude: listing.latitude, longitude: listing.longitude },
        }}
      />
    </main>
  );
}
