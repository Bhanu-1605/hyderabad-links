"use client";

import { useActionState } from "react";
import { deleteMediaAction } from "@/app/account/listings/[id]/photos/actions";
import type { MediaFormState } from "@/app/account/listings/[id]/photos/form-state";

/** Removes one file. The confirmation is the browser's, because the deletion cannot be undone. */
export function RemoveMediaButton({
  listingId,
  mediaId,
  what,
}: {
  listingId: number;
  mediaId: number;
  what: string;
}) {
  const [state, formAction, pending] = useActionState<MediaFormState>(
    () => deleteMediaAction(listingId, mediaId),
    { rev: 0 },
  );

  return (
    <form
      action={formAction}
      onSubmit={(event) => {
        if (!window.confirm(`Remove this ${what}? This cannot be undone.`)) event.preventDefault();
      }}
    >
      <button
        type="submit"
        disabled={pending}
        className="cursor-pointer rounded-full border border-line bg-white px-3 py-1.5 text-xs font-bold text-red-700 hover:border-red-300 hover:bg-red-50 disabled:cursor-default disabled:opacity-60"
      >
        {pending ? "Removing..." : "Remove"}
      </button>
      {state.message ? (
        <p role="alert" className="mt-1 text-xs font-medium text-red-700">
          {state.message}
        </p>
      ) : null}
    </form>
  );
}
