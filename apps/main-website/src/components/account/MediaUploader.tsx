"use client";

import { useActionState } from "react";
import { uploadMediaAction } from "@/app/account/listings/[id]/photos/actions";
import { emptyMediaFormState, type MediaFormState } from "@/app/account/listings/[id]/photos/form-state";

type Kind = "logo" | "photo" | "video";

/**
 * One file picker and its upload button.
 *
 * `accept` only steers the file chooser. What the file really is gets decided by the API from the
 * file's own first bytes, so a renamed file is refused there however the picker was filled in.
 */
export function MediaUploader({
  listingId,
  kind,
  label,
  accept,
  maxLabel,
}: {
  listingId: number;
  kind: Kind;
  label: string;
  accept: string;
  maxLabel: string;
}) {
  const [state, formAction, pending] = useActionState<MediaFormState, FormData>(
    uploadMediaAction.bind(null, listingId, kind),
    emptyMediaFormState,
  );

  return (
    <form action={formAction} className="mt-4 rounded-xl border border-line bg-surface p-4">
      {/* key: a new input element after each upload, which is what clears the chosen filename */}
      <label className="block text-sm font-bold text-ink" htmlFor={`${kind}-file-${state.rev}`}>
        {label}
      </label>
      <input
        key={state.rev}
        id={`${kind}-file-${state.rev}`}
        name="file"
        type="file"
        accept={accept}
        required
        className="mt-2 block w-full cursor-pointer rounded-lg border border-line bg-white px-3 py-2 text-sm file:mr-3 file:cursor-pointer file:rounded-full file:border-0 file:bg-brand-50 file:px-4 file:py-1.5 file:text-sm file:font-bold file:text-brand-700"
      />
      <p className="mt-1.5 text-xs text-muted">{maxLabel}</p>

      {state.message ? (
        <p
          role={state.success ? "status" : "alert"}
          className={
            state.success
              ? "mt-3 rounded-lg border border-brand-200 bg-brand-50 px-3 py-2 text-sm font-medium text-brand-800"
              : "mt-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-800"
          }
        >
          {state.message}
        </p>
      ) : null}

      <button
        type="submit"
        disabled={pending}
        className="mt-3 cursor-pointer rounded-full bg-brand-700 px-5 py-2.5 text-sm font-extrabold text-white hover:bg-brand-800 disabled:cursor-default disabled:opacity-60"
      >
        {pending ? "Uploading..." : "Upload"}
      </button>
    </form>
  );
}
