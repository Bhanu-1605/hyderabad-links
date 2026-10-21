import Link from "next/link";

/**
 * Initials for the circular logo, used for a business that has not uploaded one.
 */
export function initialsOf(name: string): string {
  const words = name
    .split(/\s+/)
    .map((word) => word.replace(/[^A-Za-z0-9]/g, ""))
    .filter(Boolean);
  if (words.length === 0) return "?";
  if (words.length === 1) return words[0]!.slice(0, 2).toUpperCase();
  return words
    .slice(0, 3)
    .map((word) => word.charAt(0))
    .join("")
    .toUpperCase();
}

type Props = {
  name: string;
  categoryName: string;
  href: string;
  /** The uploaded logo or first photo. Null or missing falls back to the monogram. */
  imageUrl?: string | null;
  /** `dark` is the band with the near-black background; `light` is a white section. */
  tone?: "dark" | "light";
};

/**
 * The circular-logo business card from the reference. The homepage uses it today and the search
 * results page is meant to adopt it, so the name ribbon and the monogram live here rather than on
 * either page.
 */
export function BusinessCard({ name, categoryName, href, imageUrl, tone = "light" }: Props) {
  const onDark = tone === "dark";
  return (
    <Link
      href={href}
      className="flex min-w-0 flex-col items-center px-2 pt-2.5 pb-4 text-center transition hover:-translate-y-1.5"
    >
      <div
        className={`grid aspect-square w-[min(38vw,170px)] place-items-center rounded-full lg:w-[clamp(145px,17vw,235px)] ${
          onDark
            ? "border-[6px] border-[#14272b] bg-[radial-gradient(circle,#fff_0_61%,#d8e6e8_62%_100%)] shadow-[0_0_0_1px_#21454b,0_0_26px_#0d819333] lg:border-8"
            : "border-[6px] border-white bg-[radial-gradient(circle_at_32%_25%,#fff,#e7f2f3)] shadow-[0_5px_24px_#0b4c5b26] lg:border-8"
        }`}
      >
        {imageUrl ? (
          /* Decorative: the business name is printed directly below the card. */
          <img
            src={imageUrl}
            alt=""
            loading="lazy"
            className="h-[65%] w-[65%] rounded-full border-2 border-brand-200 bg-white object-cover"
          />
        ) : (
          <span
            className={`grid h-[65%] w-[65%] place-items-center rounded-full border-2 border-brand-200 text-3xl font-black tracking-tighter lg:text-[clamp(32px,4vw,54px)] ${
              onDark ? "bg-white text-[#153c44]" : "bg-brand-50 text-brand-700"
            }`}
          >
            {initialsOf(name)}
          </span>
        )}
      </div>
      <h3
        className={`mt-4 text-sm leading-snug font-black lg:mt-5 lg:text-base ${
          onDark ? "text-white" : "text-ink"
        }`}
      >
        {name}
      </h3>
      {/* The ribbon's pointed ends are drawn with borders, as in the reference stylesheet. */}
      <span className="relative mt-2 min-w-[120px] bg-brand-700 px-4 py-1 text-[11px] font-extrabold text-white lg:min-w-[145px] lg:px-5 lg:text-[13px] before:absolute before:top-0 before:-left-2.5 before:h-0 before:w-0 before:border-y-[14px] before:border-l-[10px] before:border-y-brand-700 before:border-l-transparent before:content-[''] after:absolute after:top-0 after:-right-2.5 after:h-0 after:w-0 after:border-y-[14px] after:border-r-[10px] after:border-y-brand-700 after:border-r-transparent after:content-['']">
        {categoryName}
      </span>
    </Link>
  );
}
