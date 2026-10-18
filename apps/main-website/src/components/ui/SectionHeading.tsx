import type { ReactNode } from "react";

type Props = {
  /** The small symbol in the left rule. Decorative, so it is hidden from screen readers. */
  mark: string;
  kicker: string;
  title: ReactNode;
  /** Use on the dark band, where the rule and the text have to invert. */
  onDark?: boolean;
};

/**
 * The heading every section on the public site shares: a symbol, a dashed rule, a small kicker
 * and the real heading. Kept in one place so a later page cannot drift from the homepage.
 */
export function SectionHeading({ mark, kicker, title, onDark = false }: Props) {
  return (
    <div
      className={`mx-auto mb-8 flex max-w-[1500px] items-center gap-4 border-b border-dashed pb-4 ${
        onDark ? "border-white/30" : "border-brand-200"
      }`}
    >
      <span
        aria-hidden="true"
        className={`border-r pr-4 text-3xl leading-none sm:text-4xl ${
          onDark ? "border-white/30 text-brand-300" : "border-brand-200 text-brand-700"
        }`}
      >
        {mark}
      </span>
      <div>
        <p
          className={`text-[10px] font-black tracking-[0.22em] ${onDark ? "text-brand-300" : "text-muted"}`}
        >
          {kicker}
        </p>
        <h2
          className={`mt-1 text-2xl leading-tight font-black tracking-tight sm:text-3xl lg:text-4xl ${
            onDark ? "text-white" : "text-brand-700"
          }`}
        >
          {title}
        </h2>
      </div>
    </div>
  );
}
