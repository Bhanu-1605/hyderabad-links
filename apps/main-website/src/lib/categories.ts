import type { Category, SearchCategory } from "./types";

export function toSearchCategories(tree: Category[]): SearchCategory[] {
  const children = tree.flatMap((p) => p.children.map((c) => ({ slug: c.slug, name: c.name, terms: c.searchTerms })));
  const parents = tree.map((p) => ({ slug: p.slug, name: p.name, terms: [] as string[] }));
  return [...children, ...parents];
}

/** Finds a category (parent or sub-category) by slug. */
export function findCategory(tree: Category[], slug: string): { slug: string; name: string } | null {
  for (const parent of tree) {
    if (parent.slug === slug) return { slug: parent.slug, name: parent.name };
    const child = parent.children.find((c) => c.slug === slug);
    if (child) return { slug: child.slug, name: child.name };
  }
  return null;
}

/** Turns what the visitor typed ("plumber", "pipe repair") into a category. Exact matches win over partial ones. */
export function resolveCategory(text: string, categories: SearchCategory[]): SearchCategory | null {
  const q = text.trim().toLowerCase();
  if (!q) return null;
  const nameIs = (c: SearchCategory) => c.name.toLowerCase() === q;
  const termIs = (c: SearchCategory) => c.terms.some((t) => t.toLowerCase() === q);
  const nameHas = (c: SearchCategory) => c.name.toLowerCase().includes(q);
  const termHas = (c: SearchCategory) => c.terms.some((t) => t.toLowerCase().includes(q));
  return (
    categories.find(nameIs) ??
    categories.find(termIs) ??
    categories.find(nameHas) ??
    categories.find(termHas) ??
    null
  );
}
