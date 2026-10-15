import type { Metadata } from "next";
import Link from "next/link";
import { StatusBadge } from "@/components/StatusBadge";
import { describeStatus, listMyListings } from "@/lib/owner-api";

export const metadata: Metadata = { title: "My listings" };

const MAX_LISTINGS = 5; // same limit the API enforces

type Props = { searchParams: Promise<{ submitted?: string }> };

export default async function MyListingsPage({ searchParams }: Props) {
  const { submitted } = await searchParams;
  const result = await listMyListings();

  const addButton = "rounded-lg bg-brand-700 px-5 py-2.5 text-sm font-semibold text-white hover:bg-brand-900";

  if (!result.ok) {
    return (
      <main className="mx-auto max-w-5xl px-4 pt-8">
        <h1 className="text-2xl font-bold">My listings</h1>
        <p role="alert" className="mt-4 text-red-700">
          {result.message}
        </p>
      </main>
    );
  }

  const listings = result.data;

  return (
    <main className="mx-auto max-w-5xl px-4 pt-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold">My listings</h1>
        {listings.length > 0 && listings.length < MAX_LISTINGS ? (
          <Link href="/account/listings/new" className={addButton}>
            Add a business
          </Link>
        ) : null}
      </div>

      {submitted ? (
        <p role="status" className="mt-4 rounded-lg bg-green-50 px-4 py-3 text-sm text-green-900">
          Submitted for review. We will show your listing on the site once it is approved.
        </p>
      ) : null}

      {listings.length === 0 ? (
        <div className="mt-6 rounded-xl border border-dashed border-neutral-300 bg-white p-8 text-center">
          <h2 className="text-lg font-semibold">You have not added a business yet</h2>
          <p className="mt-1 text-neutral-600">It takes a few minutes. Customers nearby will be able to call or WhatsApp you.</p>
          <Link href="/account/listings/new" className={`${addButton} mt-5 inline-block`}>
            Add your business
          </Link>
        </div>
      ) : (
        <ul className="mt-6 grid gap-3">
          {listings.map((listing) => (
            <li key={listing.id} className="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <h2 className="text-base font-semibold">{listing.name}</h2>
                  <p className="mt-0.5 text-sm text-neutral-600">
                    {listing.categoryName} &middot; {listing.localityName}
                  </p>
                  <p className="mt-1 text-sm text-neutral-500">{describeStatus(listing.status).help}</p>
                </div>
                <StatusBadge status={listing.status} />
              </div>
              <div className="mt-3 flex flex-wrap gap-2">
                <Link
                  href={`/account/listings/${listing.id}/edit`}
                  className="rounded-lg border border-neutral-300 px-4 py-2 text-sm font-medium hover:bg-neutral-50"
                >
                  {listing.status === "draft" || listing.status === "rejected" ? "Edit and submit" : "Edit"}
                </Link>
                {listing.status === "active" ? (
                  <Link href={listing.url} className="rounded-lg px-4 py-2 text-sm font-medium text-brand-700 hover:underline">
                    View public page
                  </Link>
                ) : null}
              </div>
            </li>
          ))}
        </ul>
      )}

      {listings.length >= MAX_LISTINGS ? (
        <p className="mt-4 text-sm text-neutral-500">You have reached the limit of {MAX_LISTINGS} listings.</p>
      ) : null}
    </main>
  );
}
