#!/usr/bin/env node
// Runs every transactional check in database/sqlserver/checks against the local SQL Server
// container. Run it with `npm run db:check`.
//
// Each check inserts, asserts and rolls back, so running them leaves no data behind and they are
// safe to re-run. They need the container and the schema, so this is a local command, not a CI one:
// the .NET tests in CI deliberately need no database at all.
//
// A check fails the run by raising an error, which sqlcmd reports as a non-zero exit because of the
// -b flag. Note the -i on `docker exec`: without it sqlcmd gets no stdin and the script silently
// does nothing, which looks exactly like a pass.

import { readdirSync, readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const checksDir = path.join(root, "database", "sqlserver", "checks");
const container = process.env.MSSQL_CONTAINER ?? "hl-sqlserver";
const database = process.env.MSSQL_DATABASE ?? "hyderabad_links";

// The same default as docker-compose.yml and .env.example: a throwaway local password, never a
// production one. Set MSSQL_SA_PASSWORD to override it.
const password = process.env.MSSQL_SA_PASSWORD ?? "Hl_dev_password_change_me1";

const only = process.argv.slice(2);
const files = readdirSync(checksDir)
  .filter((f) => f.endsWith(".sql"))
  .filter((f) => only.length === 0 || only.some((pick) => f.includes(pick)))
  .sort();

if (files.length === 0) {
  console.error(only.length ? `No check matches ${only.join(", ")}` : "No checks found");
  process.exit(1);
}

let failed = 0;

for (const file of files) {
  const sql = readFileSync(path.join(checksDir, file), "utf8");
  const result = spawnSync(
    "docker",
    [
      "exec", "-i", container,
      "/opt/mssql-tools18/bin/sqlcmd",
      "-S", "localhost",
      "-U", "sa",
      "-P", password,
      "-C",                 // trust the container's self-signed certificate
      "-d", database,
      "-b",                 // a raised error becomes a non-zero exit
      "-I",                 // QUOTED_IDENTIFIER ON, which sqlcmd otherwise leaves off
    ],
    { input: sql, encoding: "utf8" },
  );

  const output = `${result.stdout ?? ""}${result.stderr ?? ""}`.trimEnd();

  if (result.error) {
    console.error(`FAIL  ${file}  -> could not run docker: ${result.error.message}`);
    failed++;
    continue;
  }

  if (result.status === 0) {
    const assertions = output.split("\n").filter((line) => line.startsWith("ok: ")).length;
    console.log(`PASS  ${file}  (${assertions} assertions)`);
  } else {
    console.error(`FAIL  ${file}`);
    console.error(output.replace(/^/gm, "      "));
    failed++;
  }
}

console.log("");
console.log(
  failed === 0
    ? `all ${files.length} database check(s) passed`
    : `${failed} of ${files.length} database check(s) failed`,
);
process.exit(failed === 0 ? 0 : 1);
