import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { checkMigrations, stripSql } from "./check-migrations.mjs";

function withMigrations(files, fn) {
  const dir = mkdtempSync(path.join(tmpdir(), "mig-"));
  try {
    for (const [name, sql] of Object.entries(files)) writeFileSync(path.join(dir, name), sql);
    return fn(dir);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

test("empty directory passes", () => {
  withMigrations({}, (d) => assert.deepEqual(checkMigrations(d), []));
});

test("valid additive migrations pass", () => {
  withMigrations(
    {
      "S0001__create_cities.sql": "CREATE TABLE cities (id bigint PRIMARY KEY);",
      "S0002__add_slug.sql": "ALTER TABLE cities ADD COLUMN slug text;",
    },
    (d) => assert.deepEqual(checkMigrations(d), []),
  );
});

test("bad file name is rejected", () => {
  withMigrations({ "create_cities.sql": "CREATE TABLE a (id int);" }, (d) =>
    assert.match(checkMigrations(d).join("\n"), /name must look like/),
  );
});

test("gap in version numbers is rejected", () => {
  withMigrations({ "S0001__a.sql": "CREATE TABLE a (id int);", "S0003__c.sql": "CREATE TABLE c (id int);" }, (d) =>
    assert.match(checkMigrations(d).join("\n"), /expected S0002/),
  );
});

test("duplicate version is rejected", () => {
  withMigrations({ "S0001__a.sql": "CREATE TABLE a (id int);", "S0001__b.sql": "CREATE TABLE b (id int);" }, (d) =>
    assert.match(checkMigrations(d).join("\n"), /Duplicate migration version 0001/),
  );
});

test("empty or comment-only file is rejected", () => {
  withMigrations({ "S0001__a.sql": "-- nothing here\n" }, (d) => assert.match(checkMigrations(d).join("\n"), /no SQL/));
});

for (const [label, sql] of [
  ["DROP TABLE", "DROP TABLE cities;"],
  ["DROP COLUMN", "ALTER TABLE cities DROP COLUMN slug;"],
  ["TRUNCATE", "TRUNCATE cities;"],
  ["DELETE without WHERE", "DELETE FROM cities;"],
  ["ALTER COLUMN TYPE", "ALTER TABLE cities ALTER COLUMN name TYPE varchar(10);"],
  ["lowercase drop table", "drop table cities;"],
]) {
  test(`destructive statement rejected: ${label}`, () => {
    withMigrations({ "S0001__x.sql": sql }, (d) => assert.match(checkMigrations(d).join("\n"), /destructive/));
  });
}

test("destructive statement allowed with an approval comment that has a reason", () => {
  withMigrations({ "S0001__x.sql": "-- approved-destructive: ticket HL-42 removes unused table\nDROP TABLE old_cities;" }, (d) =>
    assert.deepEqual(checkMigrations(d), []),
  );
});

test("approval comment without a reason does not count", () => {
  withMigrations({ "S0001__x.sql": "-- approved-destructive:\nDROP TABLE old_cities;" }, (d) =>
    assert.match(checkMigrations(d).join("\n"), /destructive/),
  );
});

test("DELETE with WHERE is allowed", () => {
  withMigrations({ "S0001__x.sql": "DELETE FROM cities WHERE id = 1;" }, (d) => assert.deepEqual(checkMigrations(d), []));
});

test("words inside comments and string literals are ignored", () => {
  withMigrations(
    { "S0001__x.sql": "-- we will not DROP TABLE here\nINSERT INTO notes(body) VALUES ('please do not drop table or truncate');" },
    (d) => assert.deepEqual(checkMigrations(d), []),
  );
});

test("stripSql handles escaped quotes and block comments", () => {
  assert.equal(stripSql("SELECT 'it''s'; /* DROP TABLE x */ SELECT 1;").replace(/\s+/g, " "), "SELECT ''; SELECT 1;");
});
