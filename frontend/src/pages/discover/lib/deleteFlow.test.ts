import {describe, expect, it} from 'vitest'
import type {PlaceTripRefDto} from '../../../shared/api/api'
import {startDelete, confirmCopy} from './deleteFlow'

const trip = (name: string, scheduledStopCount = 0): PlaceTripRefDto => ({
  tripId: `id-${name}`,
  tripName: name,
  tripPlaceId: `tp-${name}`,
  scheduledStopCount,
})

describe('startDelete', () => {
  it('skips the chooser when the place is in exactly one trip', () => {
    const t = trip('เที่ยวกาญจนบุรี')
    expect(startDelete([t])).toEqual({stage: 'confirming', trip: t})
  })

  it('asks which trip when the place is in more than one', () => {
    expect(startDelete([trip('a'), trip('b')])).toEqual({stage: 'choosing'})
  })

  it('stays idle when there is no trip to delete from', () => {
    expect(startDelete([])).toEqual({stage: 'idle'})
  })
})

describe('confirmCopy', () => {
  it('names how many stops the delete will take', () => {
    const c = confirmCopy('หอพักระยอง ฟอเรสท์', trip('เที่ยวกาญจนบุรี', 2))
    expect(c.title).toBe('เอา "หอพักระยอง ฟอเรสท์" ออกจาก เที่ยวกาญจนบุรี?')
    expect(c.warning).toBe('จุดนี้อยู่ในแผนของทริปนี้ 2 จุด — จะถูกลบไปด้วย')
  })

  it('drops the warning entirely when nothing is scheduled', () => {
    expect(confirmCopy('x', trip('t', 0)).warning).toBeNull()
  })

  it('always says the place profile survives', () => {
    expect(confirmCopy('x', trip('t', 3)).keep).toBe('โน้ต · ลิงก์รีวิว · ช่วงเวลาที่ดี ยังอยู่ในคลังของคุณ')
  })
})
