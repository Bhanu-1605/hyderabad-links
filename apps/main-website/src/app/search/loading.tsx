/**
 * Shown while the server is fetching results for a new search.
 *
 * The results are rendered on the server, so without this the browser would sit on the previous
 * page with no sign that anything is happening. The skeleton mirrors the real layout so the page
 * does not jump when the results arrive.
 *
 * `role="status"` with a visually hidden sentence is what a screen reader announces; the grey
 * blocks themselves are decorative and hidden from it.
 */
export default function SearchLoading() {
  return (
    <main className="mx-auto max-w-[1500px] px-5 pt-10 pb-20 sm:px-8 lg:px-12">
      <div role="status" className="mx-auto max-w-3xl text-center">
        <span className="sr-only">Searching for businesses…</span>
        <div aria-hidden="true" className="mx-auto h-3 w-40 animate-pulse rounded-full bg-brand-100" />
        <div aria-hidden="true" className="mx-auto mt-4 h-10 w-80 max-w-full animate-pulse rounded-lg bg-brand-100" />
        <div aria-hidden="true" className="mx-auto mt-3 h-4 w-64 max-w-full animate-pulse rounded-full bg-line" />
      </div>

      <div
        aria-hidden="true"
        className="mx-auto mt-8 h-36 max-w-4xl animate-pulse rounded-2xl border border-line bg-brand-50"
      />

      <div aria-hidden="true" className="mt-8 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
        {/* A screenful of placeholders; the real page shows up to twenty. */}
        {Array.from({ length: 6 }, (_, index) => (
          <div key={index} className="rounded-xl border border-line bg-white p-5">
            <div className="h-14 w-14 animate-pulse rounded-full bg-brand-50" />
            <div className="mt-4 h-3 w-24 animate-pulse rounded-full bg-line" />
            <div className="mt-3 h-5 w-48 max-w-full animate-pulse rounded-full bg-brand-100" />
            <div className="mt-3 h-3 w-40 max-w-full animate-pulse rounded-full bg-line" />
            <div className="mt-6 h-9 w-full animate-pulse rounded-full bg-brand-50" />
          </div>
        ))}
      </div>
    </main>
  );
}
