# Handoff — Issue #19: Place Season (multi-record) + animated weather diorama

**Date:** 2026-07-17
**Status:** Proposed (design confirmed via interactive mock)
**Issue:** [#19](https://github.com/ThodsaphonSonthiphin/MenuNest/issues/19) — per-place season periods (should-go / should-avoid) + matched-month warning on stops
**Mock (interactive):** `Place Season Redesign.dc.html` — year ribbon, period editor, and an itinerary card whose weather diorama reacts to the trip month (storm+forked-lightning+flood-ripples / clear / overcast)
**Supersedes mock:** `docs/mocks/trip-place-season-mock.html`

---

## Scope

1. **Editor** — a place has a *list* of season periods; each period = kind (`good|bad`) + a set of months + an optional note. Add/edit/delete as many as you like.
2. **Year overview (ribbon)** — 12-month strip colored good (green) / avoid (red) / neutral, with a "this month" marker. At-a-glance replacement for the chip list.
3. **Itinerary warning** — on a stop, resolve the trip's month against the place's periods (avoid wins over good) and show the matched status prominently, with an animated **weather diorama** as the visual signal.

No backend endpoint shape changes beyond persisting the periods list (see Data).

---

## Data model

```ts
type SeasonKind = 'good' | 'bad';
interface SeasonPeriod { id: string; kind: SeasonKind; months: number[]; note?: string; } // months 0..11
interface PlaceSeason { placeId: string; periods: SeasonPeriod[]; }

// shared client + report logic — put in lib/season.ts
type MonthStatus = { kind: 'bad'; period: SeasonPeriod }
               | { kind: 'good'; period: SeasonPeriod }
               | { kind: 'none' };

function monthStatus(periods: SeasonPeriod[], m: number): MonthStatus {
  const bad = periods.find(p => p.kind === 'bad'  && p.months.includes(m));  if (bad)  return { kind:'bad', period:bad };
  const good = periods.find(p => p.kind === 'good' && p.months.includes(m)); if (good) return { kind:'good', period:good };
  return { kind:'none' };
}
```

`rangeLabel(months)` (compress `[5,6,7,8,9]` → `มิ.ย.–ต.ค.`) is in the mock's logic class — lift verbatim.

---

## Components to add (`frontend/src/pages/trips/`)

### `PlaceSeasonEditor.tsx`
- Year ribbon (12 buttons). Cell fill: good `#e7f6ef`/`#1f9d6b`, bad `#fdece8`/`#d4462a`, neutral `#f4efe9`/`#a99e94`. "Now" = 2px `#2b2521` ring; draft-selected month = 2px `#ef6d2d` ring.
- Saved-period rows (kind pill + range + note + delete).
- Inline editor: good/avoid toggle, month picking **by tapping ribbon cells**, note input, save/cancel.
- State: `periods`, `editing`, `draft {kind, months, note}` — all in the mock.

### `WeatherDiorama.tsx` (canvas — the animation)
Port the mock's canvas engine into a ref + effect. It is self-contained (no libs).

```tsx
export function WeatherDiorama({ kind }: { kind: 'good'|'bad'|'none' }) {
  const ref = useRef<HTMLCanvasElement>(null);
  const kindRef = useRef(kind); kindRef.current = kind;   // read live inside RAF
  useEffect(() => {
    const cv = ref.current!; const ctx = cv.getContext('2d')!;
    let raf = 0, drops:any[] = [], ripples:any[] = [], bolt:any = null, flash = 0, lastBolt = 0;
    // genBolt(w, waterY) + frame(t): copy bodies from Place Season Redesign.dc.html's logic class,
    //   replacing `this.sceneKind` with kindRef.current and `this.canvas` with cv.
    const frame = (t:number) => { /* …draw rocks → water+crest → rain → ripples → sun(good) → lightning(bad)… */ raf = requestAnimationFrame(frame); };
    raf = requestAnimationFrame(frame);
    return () => cancelAnimationFrame(raf);
  }, []);
  return <canvas ref={ref} className="weather-diorama" />;
}
```

Scene table (per `kind`) — copy exactly from the mock:
- `bad`: waterFrac .46, rain rate 5, forked lightning + flash, ripple rings; sky `#4c5a68→#8a97a1`.
- `good`: waterFrac .26, no rain, sun-glow, occasional calm ripple; sky `#8fc7e8→#d7eefb`.
- `none`: waterFrac .32, light drizzle, no lightning; sky `#9aa2a9→#d6dade`.
Rocks = the quadratic-curve hump path (สามพันโบก), drawn dark then covered by semi-transparent water so they read as submerged in `bad`.

**Perf/a11y:** gate the RAF with `IntersectionObserver` (pause when off-screen) and honour `prefers-reduced-motion` — render one static frame instead of looping.

### Wire into `ItineraryStopCard.tsx`
- Compute `const st = monthStatus(place.periods, tripMonth)`.
- Render `<WeatherDiorama kind={st.kind} />` as the card header, then the status row
  (icon + title `เดือนนี้ควรเลี่ยง · <note>` + fix line). Card `border-top` uses the status accent.
- Scene caption pill (emoji + name) overlays the diorama top-left.

---

## Palette (existing project tokens — from the mock)

accent `#ef6d2d` / deep `#d95f22` / soft `#fdefe1` / line `#f6d9bf` · ink `#2b2521` / soft `#6b625b` / muted `#a99e94` / border `#ece4d9` · good `#1f9d6b`/`#e7f6ef`/`#bfe6d3` · bad `#d4462a`/`#fdece8`/`#f3c9bf` · page bg `radial-gradient(120% 90% at 50% -10%,#cfe3d6,#b9d3c4 40%,#a9c7b8)`. Fonts: Noto Sans Thai (UI), Spline Sans Mono (ranges/dates).

## Tests
- `lib/season.test.ts`: `monthStatus` avoid-wins-over-good; `rangeLabel` wrap/compression (`[10,11,0,1]` → `ม.ค.–ก.พ., พ.ย.–ธ.ค.`).
- Component: warning appears only when trip month ∈ an avoid period; diorama `kind` matches status.

## Out of scope
Weather diorama is illustrative (not live meteorology). Real per-day forecast stays the ADR-028/029 feature; this reflects the *authored season*, not an API reading.
