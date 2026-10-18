import Link from "next/link";

type Props = {
  /** The page being shown. May be past the end, which is why the links are clamped below. */
  page: number;
  totalPages: number;
  /** Current query string. The page number is replaced; every other filter is kept. */
  params: Record<string, string | undefined>;
  basePath: string;
};

function hrefFor(basePath: string, params: Props["params"], page: number): string {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (key !== "page" && value !== undefined && value !== "") {
      query.set(key, value);
    }
  }
  query.set("page", String(page));
  return `${basePath}?${query.toString()}`;
}

/** Previous/next paging. Renders nothing when everything fits on one page. */
export function Pagination({ page, totalPages, params, basePath }: Props) {
  if (totalPages <= 1) return null;

  const current = Math.min(Math.max(page, 1), totalPages);
  const linkClass =
    "rounded-full border border-brand-200 bg-white px-5 py-2.5 text-sm font-bold text-brand-700 hover:border-brand-700 hover:bg-brand-50";
  const disabledClass =
    "rounded-full border border-line bg-white px-5 py-2.5 text-sm font-bold text-neutral-400";

  return (
    <nav aria-label="Pagination" className="mt-8 flex items-center justify-between gap-3">
      {current > 1 ? (
        <Link href={hrefFor(basePath, params, current - 1)} rel="prev" className={linkClass}>
          <span aria-hidden="true">&larr;</span> Previous
        </Link>
      ) : (
        <span className={disabledClass} aria-disabled="true">
          <span aria-hidden="true">&larr;</span> Previous
        </span>
      )}

      <p className="text-sm font-bold text-muted" aria-live="polite">
        Page {current} of {totalPages}
      </p>

      {current < totalPages ? (
        <Link href={hrefFor(basePath, params, current + 1)} rel="next" className={linkClass}>
          Next <span aria-hidden="true">&rarr;</span>
        </Link>
      ) : (
        <span className={disabledClass} aria-disabled="true">
          Next <span aria-hidden="true">&rarr;</span>
        </span>
      )}
    </nav>
  );
}
