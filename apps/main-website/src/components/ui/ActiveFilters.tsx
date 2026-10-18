import Link from "next/link";

export type ActiveFilter = {
  /** What the visitor set, e.g. "Dentists". */
  label: string;
  /** Which part of the search it is, e.g. "Category" - read out before the label. */
  kind: string;
  /** Where "remove" goes: the same search without this filter. */
  removeHref: string;
};

/**
 * The filters currently narrowing the results, each with a way to remove it.
 *
 * Removing one is an ordinary link back to the same page with that parameter dropped, so it works
 * without JavaScript, keeps every other filter, and leaves a normal entry in browser history.
 */
export function ActiveFilters({ filters, clearAllHref }: { filters: ActiveFilter[]; clearAllHref: string }) {
  if (filters.length === 0) return null;

  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="text-xs font-black tracking-[0.14em] text-muted uppercase">Filtered by</span>
      {filters.map((filter) => (
        <Link
          key={`${filter.kind}-${filter.label}`}
          href={filter.removeHref}
          className="group inline-flex items-center gap-2 rounded-full border border-brand-200 bg-brand-50 py-1.5 pr-2 pl-3.5 text-sm font-bold text-brand-800 hover:border-brand-700 hover:bg-white"
        >
          <span>
            <span className="sr-only">{filter.kind}: </span>
            {filter.label}
          </span>
          <span
            aria-hidden="true"
            className="grid h-5 w-5 place-items-center rounded-full bg-brand-200 text-xs text-brand-800 group-hover:bg-brand-700 group-hover:text-white"
          >
            &times;
          </span>
          <span className="sr-only">(remove this filter)</span>
        </Link>
      ))}
      {filters.length > 1 ? (
        <Link href={clearAllHref} className="text-sm font-bold text-brand-700 underline-offset-2 hover:underline">
          Clear all
        </Link>
      ) : null}
    </div>
  );
}
