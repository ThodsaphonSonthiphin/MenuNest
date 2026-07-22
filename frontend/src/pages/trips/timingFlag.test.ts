// frontend/src/pages/trips/timingFlag.test.ts
import {describe, it, expect} from 'vitest'
import {flagText, severityWord} from './timingFlag'
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
  it('off-window after → waits for the upcoming window when there is one', () => {
    const f: TimingFlag = {reason: 'off-window', severity: 'suggestion', windowDir: 'after', bestStart: '06:00', bestEnd: '09:00', upcomingStart: '17:00', upcomingEnd: '19:00'}
    expect(flagText(f)).toEqual({reasonLine: 'ไปถึงหลังช่วงแนะนำ · ช่วงเหมาะ 06:00–09:00', fixLine: 'รอช่วง 17:00–19:00'})
  })
  it('off-window before with no upcoming window falls back to "move earlier"', () => {
    const f: TimingFlag = {reason: 'off-window', severity: 'suggestion', windowDir: 'before', bestStart: '17:30', bestEnd: '18:30'}
    expect(flagText(f)).toEqual({reasonLine: 'ไปถึงก่อนช่วงแนะนำ · ช่วงเหมาะ 17:30–18:30', fixLine: 'เลื่อนสตอปนี้ให้เร็วขึ้น'})
  })
})

describe('severityWord', () => {
  it('maps severity to a Thai word', () => {
    expect(severityWord('problem')).toBe('ต้องแก้')
    expect(severityWord('suggestion')).toBe('น่าปรับ')
  })
})
