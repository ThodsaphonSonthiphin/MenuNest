import type {PlaceTripRefDto} from '../../../shared/api/api'

/**
 * ADR-166/168. A Discover pin is a read-time group over N TripPlace rows, so a delete has to
 * name a Trip. With exactly one Trip there is nothing to name, so the chooser is skipped.
 */
export type DeleteFlow =
  | {stage: 'idle'}
  | {stage: 'choosing'}
  | {stage: 'confirming'; trip: PlaceTripRefDto}

export function startDelete(trips: readonly PlaceTripRefDto[]): DeleteFlow {
  if (trips.length === 0) return {stage: 'idle'} // ADR-155 says unreachable; not a crash if it happens
  if (trips.length === 1) return {stage: 'confirming', trip: trips[0]}
  return {stage: 'choosing'}
}

export interface ConfirmCopy {
  title: string
  /** null when nothing is scheduled — ADR-168 hides the whole warning row rather than rewording it. */
  warning: string | null
  keep: string
}

export function confirmCopy(placeName: string, trip: PlaceTripRefDto): ConfirmCopy {
  return {
    title: `เอา "${placeName}" ออกจาก ${trip.tripName}?`,
    warning:
      trip.scheduledStopCount > 0
        ? `จุดนี้อยู่ในแผนของทริปนี้ ${trip.scheduledStopCount} จุด — จะถูกลบไปด้วย`
        : null,
    keep: 'โน้ต · ลิงก์รีวิว · ช่วงเวลาที่ดี ยังอยู่ในคลังของคุณ',
  }
}
