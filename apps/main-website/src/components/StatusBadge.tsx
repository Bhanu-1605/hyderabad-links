import { describeStatus } from "@/lib/owner-api";

const tones = {
  grey: "bg-neutral-100 text-neutral-700 border-neutral-200",
  amber: "bg-amber-50 text-amber-900 border-amber-200",
  green: "bg-brand-50 text-brand-800 border-brand-200",
  red: "bg-red-50 text-red-800 border-red-200",
} as const;

export function StatusBadge({ status }: { status: string }) {
  const { label, tone } = describeStatus(status);
  return (
    <span
      className={`shrink-0 rounded-full border px-3 py-1 text-xs font-black tracking-[0.06em] uppercase ${tones[tone]}`}
    >
      {label}
    </span>
  );
}
