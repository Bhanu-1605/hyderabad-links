import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { ListingForm } from "@/components/ListingForm";
import { StatusBadge } from "@/components/StatusBadge";
import { PERMISSION_EDIT, getAdminProfile, getCategories, getListing } from "@/lib/admin-api";
import { saveListingAction } from "../../actions";

export const metadata: Metadata = { title: "Edit listing" };
export const dynamic = "force-dynamic";

type Props = { params: Promise<{ id: string }> };

export default async function EditListingPage({ params }: Props) {
  const { id: rawId } = await params;
  const id = Number.parseInt(rawId, 10);
  if (!Number.isSafeInteger(id) || id < 1) notFound();

  const [result, me, categories] = await Promise.all([getListing(id), getAdminProfile(), getCategories()]);

  if (!result.ok) {
    if (result.status === 404) notFound();
    return (
      <main className="mx-auto max-w-2xl px-4 pt-8">
        <h1 className="text-2xl font-bold">Edit listing</h1>
        <p role="alert" className="mt-4 rounded-lg bg-red-50 px-4 py-3 text-red-800">
          {result.message}
        </p>
      </main>
    );
  }

  const listing = result.data;

  // The API refuses this anyway; the page only avoids showing a form that would be rejected.
  if (me.ok && !me.data.permissions.includes(PERMISSION_EDIT)) {
    return (
      <main className="mx-auto max-w-2xl px-4 pt-8">
        <h1 className="text-2xl font-bold">{listing.name}</h1>
        <p className="mt-4 rounded-xl border border-neutral-200 bg-white p-4 text-sm text-neutral-600 shadow-sm">
          Your account cannot edit listings. Ask a super admin for the &quot;Edit any listing&quot; permission.
        </p>
      </main>
    );
  }

  return (
    <main className="mx-auto max-w-2xl px-4 pt-8 pb-12">
      <Link href={`/listings/${listing.id}`} className="text-sm font-medium text-brand-700 underline-offset-2 hover:underline">
        &larr; Back to the listing
      </Link>
      <div className="mt-3 flex items-start justify-between gap-3">
        <h1 className="text-2xl font-bold">{listing.name}</h1>
        <StatusBadge status={listing.status} />
      </div>
      <p className="mt-1 text-neutral-600">
        Editing never changes the status: a listing waiting for review still needs a decision, and a live one stays live.
      </p>

      <div className="mt-6">
        <ListingForm
          action={saveListingAction.bind(null, listing.id)}
          categories={categories}
          lockedLocalityName={listing.localityName}
          initialValues={{
            name: listing.name,
            categorySlug: listing.categorySlug ?? "",
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
    </main>
  );
}
