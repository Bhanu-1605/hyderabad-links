import { NextResponse, type NextRequest } from "next/server";
import { getNearestLocality } from "@/lib/api";

// Turns the coordinates the browser's geolocation gave us into the name of a known area, through
// the Next.js server rather than straight to the API (architecture principle 1).
//
// Nothing about the visitor is stored: the coordinates are used for this one query and forgotten.

export async function GET(request: NextRequest) {
  const lat = Number(request.nextUrl.searchParams.get("lat"));
  const lng = Number(request.nextUrl.searchParams.get("lng"));

  const usable =
    Number.isFinite(lat) && Number.isFinite(lng) && lat >= -90 && lat <= 90 && lng >= -180 && lng <= 180;
  if (!usable) {
    return NextResponse.json({ error: "bad_coordinates" }, { status: 400 });
  }

  try {
    const nearest = await getNearestLocality(lat, lng);
    if (!nearest) {
      return NextResponse.json({ error: "no_locality" }, { status: 404 });
    }
    return NextResponse.json(nearest, { headers: { "Cache-Control": "no-store" } });
  } catch {
    return NextResponse.json({ error: "lookup_failed" }, { status: 502 });
  }
}
