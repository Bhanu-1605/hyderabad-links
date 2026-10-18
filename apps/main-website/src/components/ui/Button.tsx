import Link from "next/link";
import type { ComponentProps, ReactNode } from "react";

/**
 * The site's three button looks. `primary` is the teal action, `ghost` the outlined one, and
 * `cta` the wide call-to-action used in the owner banner.
 */
export type ButtonTone = "primary" | "ghost" | "cta";

const base =
  "inline-flex items-center justify-center gap-2 rounded-full text-sm font-extrabold transition-colors disabled:opacity-60";

const tones: Record<ButtonTone, string> = {
  primary: "bg-brand-700 px-6 py-3 text-white hover:bg-brand-800",
  ghost: "border border-brand-200 bg-white px-6 py-3 text-brand-700 hover:border-brand-700 hover:bg-brand-50",
  cta: "bg-brand-700 px-7 py-4 text-base text-white hover:bg-brand-800",
};

export function buttonClass(tone: ButtonTone = "primary", extra = "") {
  return `${base} ${tones[tone]} ${extra}`.trim();
}

/** A button that submits or acts. For navigation use `ButtonLink`, so it stays a real link. */
export function Button({
  tone = "primary",
  className = "",
  children,
  ...props
}: { tone?: ButtonTone } & ComponentProps<"button">) {
  return (
    <button className={buttonClass(tone, className)} {...props}>
      {children}
    </button>
  );
}

export function ButtonLink({
  tone = "primary",
  className = "",
  children,
  ...props
}: { tone?: ButtonTone; children: ReactNode } & ComponentProps<typeof Link>) {
  return (
    <Link className={buttonClass(tone, className)} {...props}>
      {children}
    </Link>
  );
}
