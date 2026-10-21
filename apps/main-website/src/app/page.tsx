import { SearchBox } from "@/components/SearchBox";
import { BusinessCard } from "@/components/ui/BusinessCard";
import { ButtonLink } from "@/components/ui/Button";
import { CategoryCard } from "@/components/ui/CategoryCard";
import { CategoryIcon } from "@/components/ui/CategoryIcon";
import { SectionHeading } from "@/components/ui/SectionHeading";
import { getCategories, searchListings } from "@/lib/api";
import { DEFAULT_CITY } from "@/lib/brand";
import { toSearchCategories } from "@/lib/categories";

// Fetches live data, so it must render per request (never at build time).
export const dynamic = "force-dynamic";

/** How many of each thing the homepage shows. Enough to fill the grids, not enough to be a listing page. */
const CATEGORY_CARDS = 8;
const ITEMS_PER_CARD = 4;
const BUSINESS_CARDS = 8;

export default async function HomePage() {
  const tree = await getCategories();

  // Real listings, not a curated set: there is no "featured" or "trending" flag in the database
  // yet, so the homepage shows published businesses by name rather than inventing a ranking.
  const businesses = await searchListings({ city: DEFAULT_CITY, sort: "name", pageSize: BUSINESS_CARDS });

  const quickLinks = tree.slice(0, 7);

  return (
    <main>
      <section className="flex h-8 items-center gap-3 overflow-hidden bg-[#050505] px-5 text-[11px] whitespace-nowrap text-white sm:justify-center sm:text-[13px]">
        <span>Everything about a business, and all its digital profiles, in one place</span>
        <b className="text-brand-400">&bull;</b>
        <span>Chat directly with businesses on WhatsApp</span>
        <b className="text-brand-400">&bull;</b>
        <span>Find businesses that can help you in your language</span>
      </section>

      {/* Hero. The illustration is drawn in CSS - gradients and a faint grid - so it costs no image. */}
      <section className="relative isolate overflow-hidden bg-[linear-gradient(110deg,#00242c_0%,#003a47_55%,#062a34_100%)] px-5 py-14 text-white sm:py-16">
        <div
          aria-hidden="true"
          className="absolute inset-0 -z-10 bg-[radial-gradient(ellipse_at_15%_35%,#0d7b8c55,transparent_30%),radial-gradient(ellipse_at_80%_30%,#12647755,transparent_33%)]"
        />
        <div
          aria-hidden="true"
          className="absolute inset-0 -z-10 opacity-15 [background-image:linear-gradient(90deg,transparent_0_48px,#2bb3c2_49px,transparent_50px),linear-gradient(0deg,transparent_0_48px,#2bb3c2_49px,transparent_50px)] [background-size:50px_50px] [mask-image:linear-gradient(to_bottom,transparent,#000_30%,#000_85%,transparent)]"
        />

        <div className="mx-auto w-full max-w-[1050px] text-center">
          <p className="text-[11px] font-black tracking-[0.22em] text-brand-400">YOUR CITY. YOUR BUSINESSES.</p>
          <h1 className="mt-3 text-4xl leading-[1.06] font-black tracking-tight sm:text-5xl lg:text-6xl">
            Discover Hyderabad.
            <br />
            <span className="text-[#42d0dc]">Connect locally.</span>
          </h1>
          <p className="mx-auto mt-4 max-w-xl text-base text-brand-100 sm:text-[17px]">
            Find local businesses, services and professionals across Hyderabad &mdash; all in one place.
          </p>

          <div className="mx-auto mt-7 max-w-3xl text-left">
            <SearchBox categories={toSearchCategories(tree)} />
          </div>

          {quickLinks.length > 0 ? (
            <nav aria-label="Popular categories" className="mt-9 flex flex-wrap justify-center gap-x-6 gap-y-4">
              {quickLinks.map((parent) => (
                <a
                  key={parent.slug}
                  href={`/search?category=${parent.slug}`}
                  className="flex min-w-[64px] flex-col items-center gap-2 text-[11px] font-bold text-white sm:text-xs"
                >
                  <span
                    aria-hidden="true"
                    className="grid h-10 w-10 place-items-center rounded-full border border-brand-300/70 bg-white/5 text-lg font-black text-[#7de3e9] transition hover:-translate-y-0.5 hover:bg-brand-500 hover:text-white sm:h-11 sm:w-11"
                  >
                    <CategoryIcon slug={parent.slug} name={parent.name} className="h-5 w-5 sm:h-[22px] sm:w-[22px]" />
                  </span>
                  {parent.name}
                </a>
              ))}
            </nav>
          ) : null}
        </div>
      </section>

      {/*
        The category tree degrades to an empty list when the API cannot be reached (see
        getCategories), so this section is skipped rather than left as a heading over an empty grid.
      */}
      {tree.length > 0 ? (
        <section id="most-searched" className="px-5 py-14 sm:px-8 lg:px-16 lg:py-[68px]">
          <SectionHeading
            mark="&#8981;"
            kicker="FIND WHAT YOU NEED"
            title={
              <>
                Most Searched Businesses &amp; Services <span className="text-[#294d56]">in Hyderabad</span>
              </>
            }
          />
          <div className="mx-auto grid max-w-[1600px] grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4 lg:gap-7">
            {tree.slice(0, CATEGORY_CARDS).map((parent) => (
              <CategoryCard key={parent.slug} title={parent.name} items={parent.children.slice(0, ITEMS_PER_CARD)} />
            ))}
          </div>
        </section>
      ) : null}

      {businesses.items.length > 0 ? (
        <section className="bg-night px-5 py-14 text-white sm:px-8 lg:px-16 lg:py-[68px]">
          <SectionHeading
            onDark
            mark="&#9832;"
            kicker="LOCAL BUSINESSES, BIG POSSIBILITIES"
            title={
              <>
                Businesses <span className="text-brand-200">on Hyderabad Links</span>
              </>
            }
          />
          <div className="mx-auto grid max-w-[1500px] grid-cols-2 gap-x-3 gap-y-6 lg:grid-cols-4 lg:gap-8">
            {businesses.items.map((listing) => (
              <BusinessCard
                key={listing.url}
                tone="dark"
                name={listing.name}
                categoryName={listing.categoryName}
                href={listing.url}
                imageUrl={listing.logoUrl ?? listing.photoUrl}
              />
            ))}
          </div>
        </section>
      ) : null}

      <section className="flex flex-col items-start justify-between gap-6 border-t border-brand-200 bg-brand-50 px-6 py-10 sm:flex-row sm:items-center sm:px-10 lg:px-24">
        <div>
          <p className="text-[11px] font-black tracking-[0.22em] text-muted">ARE YOU A BUSINESS OWNER?</p>
          <h2 className="mt-2 text-2xl font-black text-brand-800 sm:text-3xl">
            Make your business easier to discover.
          </h2>
          <p className="mt-2 text-muted">
            Bring your business details and digital profiles together on Hyderabad Links.
          </p>
        </div>
        <ButtonLink tone="cta" href="/login" className="shrink-0">
          List your business <span aria-hidden="true">&rarr;</span>
        </ButtonLink>
      </section>
    </main>
  );
}
