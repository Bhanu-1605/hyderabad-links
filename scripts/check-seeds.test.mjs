// Sanity checks for the reference seed SQL (no database needed): slug format, uniqueness, valid parents,
// and Hyderabad-sized coordinates. Catches typos before they reach a database.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const dir = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "database", "seeds", "reference");
const SLUG = /^[a-z0-9]+(-[a-z0-9]+)*$/;

test("localities: unique valid slugs and coordinates inside the Hyderabad area", () => {
  const sql = readFileSync(path.join(dir, "R001_hyderabad_localities.sql"), "utf8");
  const rows = [...sql.matchAll(/^\s+\('([^']+)', '([^']+)', ([\d.]+), ([\d.]+)\)/gm)];
  assert.ok(rows.length >= 90, `expected at least 90 localities, found ${rows.length}`);
  const slugs = new Set();
  for (const [, slug, , lat, lng] of rows) {
    assert.match(slug, SLUG);
    assert.ok(!slugs.has(slug), `duplicate locality slug ${slug}`);
    slugs.add(slug);
    assert.ok(lat > 17.0 && lat < 17.8, `${slug}: latitude ${lat} outside Hyderabad area`);
    assert.ok(lng > 78.0 && lng < 79.0, `${slug}: longitude ${lng} outside Hyderabad area`);
  }
});

test("categories: globally unique slugs, every child has an existing parent", () => {
  const sql = readFileSync(path.join(dir, "R002_categories.sql"), "utf8");
  const [parentPart, childPart] = sql.split("-- CHILDREN");
  const parents = [...parentPart.matchAll(/^\s+\('([^']+)', '([^']+)', \d+, ARRAY/gm)].map((m) => m[1]);
  const children = [...childPart.matchAll(/^\s+\('([^']+)', '([^']+)', '([^']+)', \d+, ARRAY/gm)].map((m) => ({ parent: m[1], slug: m[2] }));
  assert.ok(parents.length >= 10);
  assert.ok(children.length >= 100);
  const all = new Set();
  for (const slug of [...parents, ...children.map((c) => c.slug)]) {
    assert.match(slug, SLUG);
    assert.ok(!all.has(slug), `duplicate category slug ${slug}`);
    all.add(slug);
  }
  for (const c of children) assert.ok(parents.includes(c.parent), `${c.slug}: unknown parent ${c.parent}`);
});
