import { BRAND_NAME } from "@/lib/brand";

export default function HomePage() {
  return (
    <main className="mx-auto flex max-w-3xl flex-col gap-4 px-6 py-24">
      <p className="text-sm font-medium uppercase tracking-widest text-neutral-500">Phase 0 placeholder</p>
      <h1 className="text-3xl font-semibold tracking-tight">{BRAND_NAME} Admin</h1>
      <p className="text-neutral-600">Sign-in and management tools arrive in a later phase.</p>
    </main>
  );
}
