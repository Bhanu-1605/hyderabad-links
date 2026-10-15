import Link from "next/link";
import { SearchBox } from "@/components/SearchBox";
import { getCategories, getLocalities } from "@/lib/api";
import { DEFAULT_CITY } from "@/lib/brand";
import { toSearchCategories } from "@/lib/categories";

// Fetches live data, so it must render per request (never at build time).
export const dynamic = "force-dynamic";

export default async function HomePage() {
  const [tree, localities] = await Promise.all([getCategories(), getLocalities(DEFAULT_CITY)]);

  return (
    <main>
      <section className="bg-brand-900 px-4 py-12 sm:py-16">
        <div className="mx-auto max-w-3xl text-center">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">Find local services in Hyderabad</h1>
          <p className="mt-2 text-brand-100">Plumbers, doctors, restaurants and more, close to you.</p>
          <div className="mt-6 text-left">
            <SearchBox categories={toSearchCategories(tree)} localities={localities ?? []} />
          </div>
        </div>
      </section>

      <section className="mx-auto max-w-5xl px-4 pt-10">
        <h2 className="text-xl font-semibold">Browse by category</h2>
        <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {tree.map((parent) => (
            <div key={parent.slug} className="rounded-xl border border-neutral-200 bg-white p-4">
              <h3 className="font-semibold">{parent.name}</h3>
              <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-sm">
                {parent.children.slice(0, 6).map((child) => (
                  <li key={child.slug}>
                    <Link href={`/search?category=${child.slug}`} className="text-brand-700 hover:underline">
                      {child.name}
                    </Link>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      </section>
    </main>
  );
}
