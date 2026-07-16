// Pure label for the capture-context banner: "วัน N" (+ optional destination).
export function addStopDayLabel(
  days: {id: string}[],
  dayId: string,
  destination?: string | null,
): string | null {
  const idx = days.findIndex((d) => d.id === dayId)
  if (idx < 0) return null
  const base = `วัน ${idx + 1}`
  const dest = destination?.trim()
  return dest && dest.length > 0 ? `${base} · ${dest}` : base
}