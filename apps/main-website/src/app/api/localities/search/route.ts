import { NextResponse, type NextRequest } from "next/server";
import { searchLocalities } from "@/lib/api";
import { DEFAULT_CITY } from "@/lib/brand";

// The location autocomplete types into this, so unlike every other page the lookup cannot be done
// while rendering on the server. This route handler is the browser's only way in: it runs on the
// Next.js server and calls the API server-to-server, so the browser still never touches the API
// directly (architecture principle 1).

/** Mirrors the API's own minimum, so a single keystroke never becomes a request. */
const MIN_LENGTH = 2;
const MAX_LENGTH = 60;
const LIMIT = 8;

export async function GET(request: NextRequest) {
  const text = (request.nextUrl.searchParams.get("q") ?? "").trim().slice(0, MAX_LENGTH);
  if (text.length < MIN_LENGTH) {
    // Not an error: the caller is simply not ready yet.
    return NextResponse.json([], { status: 200 });
  }

  try {
    const suggestions = await searchLocalities(text, DEFAULT_CITY, LIMIT);
    return NextResponse.json(suggestions, {
      // Lets the browser reuse an answer while the visitor edits what they typed.
      headers: { "Cache-Control": "private, max-age=60" },
    });
  } catch {
    // The API is down or unhappy. The combobox shows its error state; nothing internal leaks out.
    return NextResponse.json({ error: "lookup_failed" }, { status: 502 });
  }
}
