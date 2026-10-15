"use client";

import { useRouter } from "next/navigation";
import { useId, useState, type FormEvent } from "react";
import { resolveCategory } from "@/lib/categories";
import { DEFAULT_CITY } from "@/lib/brand";
import type { SearchCategory } from "@/lib/types";

type Props = {
  categories: SearchCategory[];
  localities: { slug: string; name: string }[];
  initialQuery?: string;
};

/**
 * Service + location search. Location flow: GPS if the visitor allows it, otherwise the area picker.
 * (IP-based city detection is not needed yet: Hyderabad is the only city.)
 */
export function SearchBox({ categories, localities, initialQuery = "" }: Props) {
  const router = useRouter();
  const listId = useId();
  const [query, setQuery] = useState(initialQuery);
  const [locality, setLocality] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [locating, setLocating] = useState(false);

  const suggestions = Array.from(new Set(categories.flatMap((c) => [c.name, ...c.terms])));

  function pickCategory(): SearchCategory | null {
    const category = resolveCategory(query, categories);
    if (!category) setMessage("Please choose a service from the suggestions, for example Plumbers.");
    return category;
  }

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMessage(null);
    const category = pickCategory();
    if (!category) return;
    if (!locality) {
      setMessage("Choose your area, or use your current location.");
      return;
    }
    router.push(`/${DEFAULT_CITY}/${locality}/${category.slug}`);
  }

  function findNearMe() {
    setMessage(null);
    const category = pickCategory();
    if (!category) return;
    if (!("geolocation" in navigator)) {
      setMessage("Your browser cannot share its location. Please choose your area instead.");
      return;
    }
    setLocating(true);
    navigator.geolocation.getCurrentPosition(
      (position) => {
        const lat = position.coords.latitude.toFixed(5);
        const lng = position.coords.longitude.toFixed(5);
        router.push(`/search?category=${encodeURIComponent(category.slug)}&lat=${lat}&lng=${lng}`);
      },
      () => {
        setLocating(false);
        setMessage("We could not get your location (it may be blocked). Please choose your area instead.");
      },
      { enableHighAccuracy: false, timeout: 10000, maximumAge: 300000 },
    );
  }

  return (
    <form onSubmit={onSubmit} className="w-full rounded-2xl bg-white p-3 shadow-md sm:p-4">
      <div className="grid gap-3 sm:grid-cols-[1fr_14rem_auto]">
        <div>
          <label htmlFor={`${listId}-q`} className="sr-only">
            What are you looking for?
          </label>
          <input
            id={`${listId}-q`}
            list={listId}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Plumbers, dentists, biryani..."
            autoComplete="off"
            className="w-full rounded-lg border border-neutral-300 px-3 py-2.5 text-base outline-none focus:border-brand-700 focus:ring-2 focus:ring-brand-100"
          />
          <datalist id={listId}>
            {suggestions.map((value) => (
              <option key={value} value={value} />
            ))}
          </datalist>
        </div>
        <div>
          <label htmlFor={`${listId}-area`} className="sr-only">
            Area
          </label>
          <select
            id={`${listId}-area`}
            value={locality}
            onChange={(event) => setLocality(event.target.value)}
            className="w-full rounded-lg border border-neutral-300 bg-white px-3 py-2.5 text-base outline-none focus:border-brand-700 focus:ring-2 focus:ring-brand-100"
          >
            <option value="">Choose area</option>
            {localities.map((l) => (
              <option key={l.slug} value={l.slug}>
                {l.name}
              </option>
            ))}
          </select>
        </div>
        <button
          type="submit"
          className="rounded-lg bg-brand-700 px-6 py-2.5 text-base font-semibold text-white hover:bg-brand-900"
        >
          Search
        </button>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-3 text-sm">
        <button
          type="button"
          onClick={findNearMe}
          disabled={locating}
          className="font-medium text-brand-700 underline-offset-2 hover:underline disabled:opacity-60"
        >
          {locating ? "Finding your location..." : "Use my current location"}
        </button>
        {message ? (
          <p role="alert" className="text-red-700">
            {message}
          </p>
        ) : null}
      </div>
    </form>
  );
}
