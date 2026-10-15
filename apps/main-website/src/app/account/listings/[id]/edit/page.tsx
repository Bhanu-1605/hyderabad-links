import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { ListingForm } from "@/components/ListingForm";
import { StatusBadge } from "@/components/StatusBadge";
import { SubmitForReview } from "@/components/SubmitForReview";
import { getCategories } from "@/lib/api";
import { describeStatus, getMyListing } from "@/lib/owner-api";
import { saveListingAction } from "../../../actions";

export const metadata: Metadata = { title: "Edit listing" };

type Props = { params: Promise<{ id: string }>; searchParams: Promise<{ created?: string }> };

export default async function EditListingPage({ params, searchParams }: Props) {
  const { id: rawId } = await params;
  const { created } = await searchParams;
  if (!/^\d{1,15}$/.test(rawId)) notFound();
  const id = Number(rawId);

  const [result, categories] = await Promise.all([getMyListing(id), getCategories()]);
  if (!result.ok) {
    if (result.status === 404) notFound(); // also what another owner's listing looks like
    return (
      <main className="mx-auto max-w-2xl px-4 pt-8">
        <p role="alert" className="text-red-700">
          {result.message}
        </p>
      </main>
    );
  }

  const listing = result.data;
  const canSubmit = listing.status === "draft" || listing.status === "rejected";

  return (
    <main className="mx-auto max-w-2xl px-4 pt-8">
      <Link href="/account/listings" className="text-sm text-brand-700 hover:underline">
        Back to my listings
      </Link>
      <div className="mt-2 flex items-start justify-between gap-3">
        <h1 className="text-2xl font-bold">{listing.name}</h1>
        <StatusBadge status={listing.status} />
      </div>
      <p className="mt-1 text-neutral-600">{describeStatus(listing.status).help}</p>
      {listing.status === "active" ? (
        <p className="mt-1 text-sm">
          <Link href={listing.url} className="text-brand-700 hover:underline">
            View public page
          </Link>
        </p>
      ) : null}
      {created ? (
        <p role="status" className="mt-4 rounded-lg bg-green-50 px-4 py-3 text-sm text-green-900">
          Draft saved. Add the address and a description, then submit it for review.
        </p>
      ) : null}

      <div className="mt-6">
        <ListingForm
          action={saveListingAction.bind(null, listing.id)}
          categories={categories}
          lockedLocalityName={listing.localityName}
          initialValues={{
            name: listing.name,
            categorySlug: listing.categorySlug,
            addressLine: listing.addressLine ?? "",
            pincode: listing.pincode ?? "",
            phonePrimary: listing.phonePrimary,
            phoneWhatsapp: listing.phoneWhatsapp ?? "",
            websiteUrl: listing.websiteUrl ?? "",
            description: listing.description ?? "",
          }}
          submitLabel="Save changes"
        />
      </div>

      {canSubmit ? (
        <div className="mt-8">
          <SubmitForReview listingId={listing.id} />
        </div>
      ) : null}
    </main>
  );
}
