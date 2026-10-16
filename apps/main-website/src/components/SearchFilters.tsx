import type { Category, Locality, SearchCriteria } from "@/lib/types";

type Props = {
  criteria: SearchCriteria;
  categories: Category[];
  localities: Locality[];
};

const inputClass =
  "w-full rounded-lg border border-neutral-300 bg-white px-3 py-2.5 text-base outline-none focus:border-brand-700 focus:ring-2 focus:ring-brand-100";

/**
 * Keyword and filter controls for the search page.
 *
 * A plain GET form, so every filter ends up in the URL (shareable, bookmarkable, and readable by
 * the server component that renders the results). It needs no client-side JavaScript to work.
 */
export function SearchFilters({ criteria, categories, localities }: Props) {
  // Sorting by distance is only offered when the URL carries coordinates, because the API rejects it otherwise.
  const hasCoordinates = criteria.lat !== undefined && criteria.lng !== undefined;

  return (
    <form method="GET" action="/search" className="rounded-2xl bg-white p-3 shadow-md sm:p-4">
      {/* Changing a filter starts again at the first page. */}
      <input type="hidden" name="page" value="1" />
      {hasCoordinates ? (
        <>
          <input type="hidden" name="lat" value={criteria.lat} />
          <input type="hidden" name="lng" value={criteria.lng} />
          {criteria.radiusKm !== undefined ? <input type="hidden" name="radiusKm" value={criteria.radiusKm} /> : null}
        </>
      ) : null}

      <div className="grid gap-3 sm:grid-cols-[1fr_auto]">
        <div>
          <label htmlFor="search-q" className="sr-only">
            Business, service or keyword
          </label>
          <input
            id="search-q"
            name="q"
            type="search"
            defaultValue={criteria.q ?? ""}
            placeholder="Business name, service or keyword"
            autoComplete="off"
            className={inputClass}
          />
        </div>
        <button
          type="submit"
          className="rounded-lg bg-brand-700 px-6 py-2.5 text-base font-semibold text-white hover:bg-brand-900"
        >
          Search
        </button>
      </div>

      <div className="mt-3 grid gap-3 sm:grid-cols-3">
        <div>
          <label htmlFor="search-category" className="block text-sm font-medium text-neutral-700">
            Category
          </label>
          <select id="search-category" name="category" defaultValue={criteria.category ?? ""} className={`mt-1 ${inputClass}`}>
            <option value="">All categories</option>
            {categories.map((parent) => (
              <optgroup key={parent.slug} label={parent.name}>
                <option value={parent.slug}>All {parent.name.toLowerCase()}</option>
                {parent.children.map((child) => (
                  <option key={child.slug} value={child.slug}>
                    {child.name}
                  </option>
                ))}
              </optgroup>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="search-locality" className="block text-sm font-medium text-neutral-700">
            Area
          </label>
          <select id="search-locality" name="locality" defaultValue={criteria.locality ?? ""} className={`mt-1 ${inputClass}`}>
            <option value="">All areas</option>
            {localities.map((locality) => (
              <option key={locality.slug} value={locality.slug}>
                {locality.name}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="search-sort" className="block text-sm font-medium text-neutral-700">
            Sort by
          </label>
          <select id="search-sort" name="sort" defaultValue={criteria.sort ?? ""} className={`mt-1 ${inputClass}`}>
            <option value="">Best match</option>
            {hasCoordinates ? <option value="distance">Nearest first</option> : null}
            <option value="name">Name (A to Z)</option>
          </select>
        </div>
      </div>
    </form>
  );
}
