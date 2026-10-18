"use client";

import { useActionState } from "react";
import { submitListingAction } from "@/app/account/actions";
import type { SubmitState } from "@/app/account/form-state";

export function SubmitForReview({ listingId }: { listingId: number }) {
  const [state, formAction, pending] = useActionState<SubmitState>(
    () => submitListingAction(listingId),
    { fieldErrors: {} },
  );

  return (
    <form action={formAction} className="rounded-2xl border border-brand-200 bg-brand-50 p-6">
      <h2 className="text-lg font-black text-brand-800">Ready to go live?</h2>
      <p className="mt-1.5 text-sm text-muted">
        Save your changes first: submitting sends the last saved version. We will check it, and it
        will appear on the site once approved.
      </p>
      {state.message ? (
        <p
          role="alert"
          className="mt-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-800"
        >
          {state.message}
        </p>
      ) : null}
      <button
        type="submit"
        disabled={pending}
        className="mt-4 cursor-pointer rounded-full bg-brand-700 px-6 py-3 text-sm font-extrabold text-white hover:bg-brand-800 disabled:cursor-default disabled:opacity-60"
      >
        {pending ? "Submitting..." : "Submit for review"}
      </button>
    </form>
  );
}
