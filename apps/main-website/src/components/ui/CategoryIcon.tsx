import type { ReactNode } from "react";

/**
 * Line icons for the top-level categories.
 *
 * Hand-drawn as inline SVG rather than pulled from an icon package: the set is small and fixed, so
 * a runtime dependency would be a lot of machinery for seventeen shapes. They are stroke-only and
 * use `currentColor`, so they take the colour of whatever they sit in and need no palette of their
 * own.
 *
 * Keyed by the real category slug. Anything not listed falls back to the first letter of its name,
 * so adding a category to the database never leaves a hole in the page.
 */
const ICONS: Record<string, ReactNode> = {
  // A nut and spanner shaft: trades rather than the building itself.
  "home-services": (
    <>
      <circle cx="8" cy="8" r="3.2" />
      <path d="m10.3 10.3 8.7 8.7" />
    </>
  ),
  "restaurants-and-food": (
    <>
      <path d="M7 3v6a2 2 0 0 0 4 0V3" />
      <path d="M9 11v10" />
      <path d="M16.5 21V3c2.2 1.8 2.2 6.2 0 8" />
    </>
  ),
  healthcare: (
    <>
      <circle cx="12" cy="12" r="8.5" />
      <path d="M12 8v8M8 12h8" />
    </>
  ),
  "beauty-and-salons": (
    <>
      <circle cx="7" cy="18" r="2.5" />
      <circle cx="17" cy="18" r="2.5" />
      <path d="M8.8 16.2 18 4M15.2 16.2 6 4" />
    </>
  ),
  "education-and-training": (
    <>
      <path d="M12 5 2.5 9.5 12 14l9.5-4.5L12 5Z" />
      <path d="M6.5 11.8V16c0 1.7 2.5 3 5.5 3s5.5-1.3 5.5-3v-4.2" />
    </>
  ),
  automotive: (
    <>
      <path d="M3 16v-4l1.8-4.5h14.4L21 12v4Z" />
      <circle cx="7.5" cy="16.5" r="1.8" />
      <circle cx="16.5" cy="16.5" r="1.8" />
    </>
  ),
  "real-estate": <path d="M3.5 10.5 12 4l8.5 6.5V20h-17Z" />,
  // A ruler: interiors and fit-out work.
  "construction-and-interiors": (
    <>
      <rect x="3" y="8" width="18" height="8" rx="1.5" />
      <path d="M7.5 8v3M12 8v3M16.5 8v3" />
    </>
  ),
  // Two interlocking rings.
  "events-and-weddings": (
    <>
      <circle cx="9" cy="14" r="5" />
      <circle cx="15" cy="14" r="5" />
    </>
  ),
  shopping: (
    <>
      <path d="M5 8h14l-1 12H6Z" />
      <path d="M9 8V6a3 3 0 0 1 6 0v2" />
    </>
  ),
  "professional-services": (
    <>
      <rect x="3" y="8" width="18" height="11" rx="2" />
      <path d="M9 8V6.5A1.5 1.5 0 0 1 10.5 5h3A1.5 1.5 0 0 1 15 6.5V8" />
    </>
  ),
  "it-and-digital": (
    <>
      <rect x="3" y="5" width="18" height="11" rx="2" />
      <path d="M9 20h6M12 16v4" />
    </>
  ),
  "travel-and-hotels": <path d="M21 12 3 19l4-7-4-7Z" />,
  pets: (
    <>
      <circle cx="8" cy="8" r="1.8" />
      <circle cx="12" cy="6.5" r="1.8" />
      <circle cx="16" cy="8" r="1.8" />
      <path d="M12 11c-3 0-5 2.2-5 4.6 0 1.7 1.3 2.9 3 2.9h4c1.7 0 3-1.2 3-2.9C17 13.2 15 11 12 11Z" />
    </>
  ),
  "business-services": (
    <>
      <rect x="5" y="4" width="14" height="17" rx="2" />
      <path d="M9.5 4h5v2.5h-5Z" />
      <path d="M8.5 11h7M8.5 15h5" />
    </>
  ),
  "fitness-and-sports": <path d="M4 9v6M7 7v10M17 7v10M20 9v6M7 12h10" />,
  "movers-and-transport": (
    <>
      <path d="M3 16V7h11v9" />
      <path d="M14 11h4l3 3v2h-7" />
      <circle cx="7" cy="17.5" r="1.8" />
      <circle cx="17" cy="17.5" r="1.8" />
    </>
  ),
};

type Props = { slug: string; name: string; className?: string };

export function CategoryIcon({ slug, name, className }: Props) {
  const icon = ICONS[slug];

  // No icon drawn for this category yet: the initial still reads as something deliberate.
  if (!icon) {
    return (
      <span aria-hidden="true" className={className}>
        {name.charAt(0)}
      </span>
    );
  }

  return (
    <svg
      aria-hidden="true"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.6}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
    >
      {icon}
    </svg>
  );
}
