"use client";

import { useActionState } from "react";
import { moveMediaAction } from "@/app/account/listings/[id]/photos/actions";
import type { MediaFormState } from "@/app/account/listings/[id]/photos/form-state";

/**
 * Moves one photo one place earlier or later. Two buttons rather than drag-and-drop: the order only
 * matters a little, and a pair of buttons works with a keyboard and on a phone without any of the
 * machinery dragging would need.
 */
export function MoveMediaButton({
  listingId,
  mediaId,
  direction,
  disabled,
}: {
  listingId: number;
  mediaId: number;
  direction: "up" | "down";
  disabled: boolean;
}) {
  const [state, formAction, pending] = useActionState<MediaFormState>(
    () => moveMediaAction(listingId, mediaId, direction),
    { rev: 0 },
  );

  return (
    <form action={formAction} className="inline">
      <button
        type="submit"
        disabled={disabled || pending}
        aria-label={direction === "up" ? "Move earlier" : "Move later"}
        title={state.message ?? (direction === "up" ? "Move earlier" : "Move later")}
        className="cursor-pointer rounded-full border border-line bg-white px-2.5 py-1.5 text-xs font-bold text-brand-700 hover:border-brand-700 hover:bg-brand-50 disabled:cursor-default disabled:opacity-40"
      >
        <span aria-hidden="true">{direction === "up" ? "←" : "→"}</span>
      </button>
    </form>
  );
}
