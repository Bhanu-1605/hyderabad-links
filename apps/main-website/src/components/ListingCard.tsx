import Link from "next/link";
import type { SearchListing } from "@/lib/types";
import { formatDistance, telHref, whatsappHref } from "@/lib/format";

export function ListingCard({ listing }: { listing: SearchListing }) {
  return (
    <li className="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="text-base font-semibold">
            <Link href={listing.url} className="hover:text-brand-700 hover:underline">
              {listing.name}
            </Link>
          </h3>
          <p className="mt-0.5 text-sm text-neutral-600">
            {listing.categoryName} &middot; {listing.localityName}
          </p>
          {listing.addressLine ? <p className="mt-1 text-sm text-neutral-500">{listing.addressLine}</p> : null}
        </div>
        {/* Only shown when the search knew where the visitor was. */}
        {listing.distanceMetres !== null ? (
          <span className="shrink-0 rounded-full bg-brand-50 px-2.5 py-1 text-xs font-medium text-brand-900">
            {formatDistance(listing.distanceMetres)}
          </span>
        ) : null}
      </div>
      <div className="mt-3 flex gap-2">
        <a
          href={telHref(listing.phonePrimary)}
          className="rounded-lg bg-brand-700 px-4 py-2 text-sm font-medium text-white hover:bg-brand-900"
        >
          Call
        </a>
        {listing.phoneWhatsapp ? (
          <a
            href={whatsappHref(listing.phoneWhatsapp)}
            target="_blank"
            rel="noopener noreferrer"
            className="rounded-lg border border-neutral-300 px-4 py-2 text-sm font-medium hover:bg-neutral-50"
          >
            WhatsApp
          </a>
        ) : null}
      </div>
    </li>
  );
}
