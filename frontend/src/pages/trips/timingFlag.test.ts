// frontend/src/pages/trips/timingFlag.test.ts
import {describe, it, expect} from 'vitest'
import {flagText} from './timingFlag'
import type {TimingFlag} from './hooks/useSchedule'

describe('flagText', () => {
  it('overflow', () => {
    const f: TimingFlag = {reason: 'overflow', severity: 'problem', arrival: '00:20'}
    expect(flagText(f)).toEqual({reasonLine: 'แผนวันนี้ยาวข้ามเที่ยงคืน (ถึง 00:20)', fixLine: 'ตัดจุดแวะออก หรือเริ่มวันให้เร็วขึ้น'})
  })
  it('closed before-open', () => {
    const f: TimingFlag = {reason: 'closed', severity: 'problem', closedKind: 'before-open', reopenAt: '10:00'}
    expect(flagText(f)).toEqual({reasonLine: 'ยังไม่เปิดตอนไปถึง · เปิด 10:00', fixLine: 'เลื่อนสตอปนี้ไปช่วงสาย'})
  })
  it('closed on-break', () => {
    const f: TimingFlag = {reason: 'closed', severity: 'problem', closedKind: 'on-break', reopenAt: '17:00'}
    expect(flagText(f)).toEqual({reasonLine: 'ปิดพักช่วงนี้ · เปิดอีกที 17:00', fixLine: 'เลี่ยงช่วงพักกลางวัน'})
  })
  it('closed after-close', () => {
    const f: TimingFlag = {reason: 'closed', severity: 'problem', closedKind: 'after-close'}
    expect(flagText(f)).toEqual({reasonLine: 'ร้านปิดแล้วตอนไปถึง', fixLine: 'เลื่อนสตอปนี้ให้เร็วขึ้น'})
  })
  it('closed all-day', () => {
    const f: TimingFlag = {reason: 'closed', severity: 'problem', closedKind: 'all-day'}
    expect(flagText(f)).toEqual({reasonLine: 'ร้านปิดทั้งวันนี้', fixLine: 'ย้ายไปวันอื่น หรือเอาออก'})
  })
  it('off-window after', () => {
    const f: TimingFlag = {reason: 'off-window', severity: 'suggestion', windowDir: 'after', bestStart: '12:00', bestEnd: '13:00'}
    expect(flagText(f)).toEqual({reasonLine: 'ไปถึงหลังช่วงแนะนำ · ช่วงเหมาะ 12:00–13:00', fixLine: 'เลื่อนสตอปนี้ขึ้นก่อนหน้า'})
  })
  it('off-window before', () => {
    const f: TimingFlag = {reason: 'off-window', severity: 'suggestion', windowDir: 'before', bestStart: '17:30', bestEnd: '18:30'}
    expect(flagText(f)).toEqual({reasonLine: 'ไปถึงก่อนช่วงแนะนำ · ช่วงเหมาะ 17:30–18:30', fixLine: 'เลื่อนสตอปนี้ไปช่วงหลัง'})
  })
})
