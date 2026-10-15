import type { Metadata } from "next";
import Link from "next/link";
import { ListingForm } from "@/components/ListingForm";
import { getCategories, getLocalities } from "@/lib/api";
import { DEFAULT_CITY } from "@/lib/brand";
import { saveListingAction } from "../../actions";

export const metadata: Metadata = { title: "Add your business" };

export default async function NewListingPage() {
  const [categories, localities] = await Promise.all([getCategories(), getLocalities(DEFAULT_CITY)]);
  return (
    <main className="mx-auto max-w-2xl px-4 pt-8">
      <Link href="/account/listings" className="text-sm text-brand-700 hover:underline">
        Back to my listings
      </Link>
      <h1 className="mt-2 text-2xl font-bold">Add your business</h1>
      <p className="mt-1 text-neutral-600">Start with the basics. It stays private as a draft until you submit it for review.</p>
      <div className="mt-6">
        <ListingForm
          action={saveListingAction.bind(null, null)}
          categories={categories}
          localities={localities ?? []}
          initialValues={{}}
          submitLabel="Save draft"
        />
      </div>
    </main>
  );
}
