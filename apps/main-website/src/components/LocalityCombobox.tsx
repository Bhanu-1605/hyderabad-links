"use client";

import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import type { LocalitySuggestion, NearestLocality } from "@/lib/types";

/** What the visitor settled on. Coordinates are present only when their own location produced it. */
export type LocalityChoice = {
  slug: string;
  name: string;
  coords?: { lat: number; lng: number };
};

type Props = {
  /**
   * When set, the chosen slug is submitted under this field name by an ordinary form. The search
   * page needs this because its filters are a plain GET form.
   */
  name?: string;
  label?: string;
  /** Hide the label visually (the home page search bar has no visible labels). */
  hideLabel?: boolean;
  /** Lets a form style this label like its own (the search filters use small caps). */
  labelClassName?: string;
  initialSlug?: string;
  initialName?: string;
  /** Called with the choice, or null once the visitor edits the text again. */
  onChange?: (choice: LocalityChoice | null) => void;
};

/** How long to wait after the last keystroke before asking the server. */
const DEBOUNCE_MS = 250;

/** Beyond this, the nearest area we know about is probably not where the visitor is standing. */
const FAR_AWAY_METRES = 5000;

// Room on the right for the clear button, which sits inside the field.
const inputClass =
  "w-full rounded-lg border border-line bg-white py-2.5 pr-10 pl-3 text-base text-ink outline-none focus:border-brand-700 focus:ring-2 focus:ring-brand-100";

/**
 * Accessible area picker: type part of a name and choose from the suggestions, or let the browser
 * share your location and have it named for you.
 *
 * Two rules matter more than the rest:
 *
 * 1. Only a suggestion the visitor actually chose becomes a filter, and only ever as its slug.
 *    Text that was merely typed is never sent as a locality - "gachi" is not an area, and silently
 *    guessing which one was meant would show the wrong results under a confident-looking heading.
 * 2. Geolocation is asked for when the button is clicked and at no other time, so simply opening
 *    the page never triggers a permission prompt.
 */
export function LocalityCombobox({
  name,
  label = "Area",
  hideLabel = false,
  labelClassName,
  initialSlug,
  initialName,
  onChange,
}: Props) {
  const baseId = useId();
  const listId = `${baseId}-list`;
  const inputId = `${baseId}-input`;
  const statusId = `${baseId}-status`;

  const [text, setText] = useState(initialName ?? "");
  const [chosen, setChosen] = useState<LocalityChoice | null>(
    initialSlug && initialName ? { slug: initialSlug, name: initialName } : null,
  );
  const [suggestions, setSuggestions] = useState<LocalitySuggestion[]>([]);
  const [open, setOpen] = useState(false);
  const [highlighted, setHighlighted] = useState(-1);
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);
  const [locating, setLocating] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  // Every lookup gets a number. A slow earlier answer arriving after a newer one is dropped rather
  // than overwriting it, which is what makes fast typing feel correct.
  const latestLookup = useRef(0);

  useEffect(() => {
    const query = text.trim();

    // Nothing to look up: too short, or the text is exactly what was already chosen.
    if (query.length < 2 || query === chosen?.name) {
      setSuggestions([]);
      setLoading(false);
      setFailed(false);
      return;
    }

    const lookup = ++latestLookup.current;
    const controller = new AbortController();
    setLoading(true);
    setFailed(false);

    const timer = setTimeout(async () => {
      try {
        const response = await fetch(`/api/localities/search?q=${encodeURIComponent(query)}`, {
          signal: controller.signal,
        });
        if (!response.ok) throw new Error(`lookup failed with status ${response.status}`);
        const rows = (await response.json()) as LocalitySuggestion[];
        if (lookup !== latestLookup.current) return; // a newer lookup has already answered
        setSuggestions(rows);
        setHighlighted(rows.length > 0 ? 0 : -1);
        setOpen(true);
        setLoading(false);
      } catch {
        if (controller.signal.aborted || lookup !== latestLookup.current) return;
        setSuggestions([]);
        setFailed(true);
        setLoading(false);
        setOpen(true);
      }
    }, DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [text, chosen?.name]);

  function choose(suggestion: LocalitySuggestion) {
    const choice = { slug: suggestion.slug, name: suggestion.name };
    setChosen(choice);
    setText(suggestion.name);
    setSuggestions([]);
    setOpen(false);
    setHighlighted(-1);
    setNotice(null);
    onChange?.(choice);
  }

  function clearChoice(nextText: string) {
    setText(nextText);
    setNotice(null);
    if (chosen) {
      setChosen(null);
      onChange?.(null);
    }
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      if (suggestions.length === 0) return;
      if (!open) {
        setOpen(true);
        return;
      }
      const step = event.key === "ArrowDown" ? 1 : -1;
      setHighlighted((current) => (current + step + suggestions.length) % suggestions.length);
      return;
    }

    if (event.key === "Enter") {
      // Only intercept Enter while a suggestion is highlighted, so Enter still submits the form
      // when the visitor has finished with this field.
      if (open && highlighted >= 0 && suggestions[highlighted]) {
        event.preventDefault();
        choose(suggestions[highlighted]);
      }
      return;
    }

    if (event.key === "Escape") {
      if (open) {
        event.preventDefault();
        setOpen(false);
        setHighlighted(-1);
      }
      return;
    }

    if (event.key === "Tab") {
      setOpen(false);
    }
  }

  function useMyLocation() {
    setNotice(null);
    // Truthiness rather than "geolocation" in navigator: some browsers expose the property but
    // leave it empty, and an insecure origin can too.
    if (!navigator.geolocation) {
      setNotice("Your browser cannot share its location. Please type your area instead.");
      return;
    }

    setLocating(true);
    navigator.geolocation.getCurrentPosition(
      async (position) => {
        const { latitude, longitude } = position.coords;
        try {
          const response = await fetch(
            `/api/localities/nearest?lat=${latitude.toFixed(5)}&lng=${longitude.toFixed(5)}`,
          );
          if (!response.ok) throw new Error(`nearest lookup failed with status ${response.status}`);
          const nearest = (await response.json()) as NearestLocality;

          const choice = { slug: nearest.slug, name: nearest.name, coords: { lat: latitude, lng: longitude } };
          setChosen(choice);
          setText(nearest.name);
          setSuggestions([]);
          setOpen(false);
          setHighlighted(-1);
          onChange?.(choice);
          if (nearest.distanceMetres > FAR_AWAY_METRES) {
            const km = (nearest.distanceMetres / 1000).toFixed(1);
            setNotice(`The nearest area we cover is ${nearest.name}, about ${km} km away.`);
          }
        } catch {
          setNotice("We found your location but could not name your area. Please type it instead.");
        } finally {
          setLocating(false);
        }
      },
      () => {
        setLocating(false);
        setNotice("We could not get your location (it may be blocked). Please type your area instead.");
      },
      { enableHighAccuracy: false, timeout: 10000, maximumAge: 300000 },
    );
  }

  const showList = open && (suggestions.length > 0 || failed || (!loading && text.trim().length >= 2));
  const unconfirmed = text.trim().length > 0 && !chosen;

  return (
    <div
      className="relative"
      onBlur={(event) => {
        // Closing on blur, but not while focus is simply moving to one of the options.
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          setOpen(false);
          setHighlighted(-1);
        }
      }}
    >
      {/* The chosen slug - never the typed text - is what a plain form submits. */}
      {name ? <input type="hidden" name={name} value={chosen?.slug ?? ""} /> : null}

      <label htmlFor={inputId} className={hideLabel ? "sr-only" : labelClassName ?? "block text-sm font-medium text-ink"}>
        {label}
      </label>
      <div className={hideLabel ? "relative" : "relative mt-1.5"}>
        <input
          id={inputId}
          type="text"
          role="combobox"
          aria-expanded={showList}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={highlighted >= 0 && showList ? `${listId}-${highlighted}` : undefined}
          aria-describedby={statusId}
          autoComplete="off"
          value={text}
          placeholder="Choose a location"
          onChange={(event) => clearChoice(event.target.value)}
          onKeyDown={onKeyDown}
          onFocus={() => {
            if (suggestions.length > 0) setOpen(true);
          }}
          className={inputClass}
        />

        {/* A visible way out of a chosen area, rather than making the visitor delete the text. */}
        {text ? (
          <button
            type="button"
            onClick={() => {
              clearChoice("");
              setSuggestions([]);
              setOpen(false);
              setHighlighted(-1);
              document.getElementById(inputId)?.focus();
            }}
            className="absolute top-1/2 right-2 grid h-6 w-6 -translate-y-1/2 cursor-pointer place-items-center rounded-full bg-brand-100 text-sm text-brand-800 hover:bg-brand-700 hover:text-white"
          >
            <span aria-hidden="true">&times;</span>
            <span className="sr-only">Clear the chosen location</span>
          </button>
        ) : null}
      </div>

      {showList ? (
        <ul
          id={listId}
          role="listbox"
          aria-label={`${label} suggestions`}
          className="absolute z-20 mt-1 max-h-72 w-full overflow-auto rounded-lg border border-neutral-200 bg-white py-1 shadow-lg"
        >
          {failed ? (
            <li className="px-3 py-2 text-sm text-red-700">We could not load areas just now. Please try again.</li>
          ) : suggestions.length === 0 ? (
            <li className="px-3 py-2 text-sm text-neutral-600">No area matched &ldquo;{text.trim()}&rdquo;.</li>
          ) : (
            suggestions.map((suggestion, index) => (
              <li
                key={suggestion.slug}
                id={`${listId}-${index}`}
                role="option"
                aria-selected={index === highlighted}
                // Chosen on mousedown so the choice happens before the input loses focus.
                onMouseDown={(event) => {
                  event.preventDefault();
                  choose(suggestion);
                }}
                onMouseEnter={() => setHighlighted(index)}
                className={`cursor-pointer px-3 py-2 text-base ${
                  index === highlighted ? "bg-brand-100 text-brand-900" : "text-neutral-800"
                }`}
              >
                {suggestion.name}
                <span className="ml-2 text-sm text-neutral-500">
                  {suggestion.cityName}, {suggestion.state}
                </span>
              </li>
            ))
          )}
        </ul>
      ) : null}

      <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
        <button
          type="button"
          onClick={useMyLocation}
          disabled={locating}
          className="font-medium text-brand-700 underline-offset-2 hover:underline disabled:opacity-60"
        >
          {locating ? "Finding your location..." : "Use my current location"}
        </button>
        {/* One live region for everything a screen reader should hear about this field. */}
        <p id={statusId} aria-live="polite" className={notice ? "text-red-700" : "text-neutral-500"}>
          {notice
            ? notice
            : loading
              ? "Looking for areas..."
              : unconfirmed
                ? "Choose an area from the list to filter by it."
                : ""}
        </p>
      </div>
    </div>
  );
}
