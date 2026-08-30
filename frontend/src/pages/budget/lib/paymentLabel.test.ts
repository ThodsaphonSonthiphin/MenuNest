import {describe, expect, it} from 'vitest'
import {
  payActionWord, payButtonLabel, paymentPillTone, paymentProgress, shortfallLine,
} from './paymentLabel'

describe('payActionWord', () => {
  // menunest-212: the word follows the ACCOUNT type, not the surface it is
  // drawn on — the envelope card, the transaction row and the dialog title
  // all read the same one.
  it('is จ่ายบัตร for a credit card', () => {
    expect(payActionWord('Credit')).toBe('จ่ายบัตร')
  })
  it('is จ่ายค่างวด for a loan', () => {
    expect(payActionWord('Loan')).toBe('จ่ายค่างวด')
  })
})

describe('payButtonLabel', () => {
  // menunest-212: one action, the label follows the account type.
  it('says จ่ายบัตร on a credit card', () => {
    expect(payButtonLabel('Credit')).toBe('฿ จ่ายบัตร')
  })
  it('says จ่ายค่างวด on a loan', () => {
    expect(payButtonLabel('Loan')).toBe('฿ จ่ายค่างวด')
  })
})

describe('shortfallLine', () => {
  it('reads จ่ายเต็มได้ when fully funded', () => {
    expect(shortfallLine(0)).toEqual({text: 'จ่ายเต็มได้', tone: 'ok'})
  })
  it('names the gap when short', () => {
    expect(shortfallLine(20000)).toEqual({text: 'ขาดอีก ฿20,000.00', tone: 'short'})
  })
  it('renders nothing for a non-payment envelope', () => {
    expect(shortfallLine(null)).toBeNull()
  })
  it('renders nothing when the field is absent altogether', () => {
    expect(shortfallLine(undefined)).toBeNull()
  })
})

describe('paymentProgress', () => {
  // A Payment envelope has no target (targetType 'None'), so
  // targetProgressFraction is null and the shared bar would sit at 0% while
  // the card reads จ่ายเต็มได้. The bar tracks funded-against-owed instead.
  it('is full and green when the envelope covers the card', () => {
    expect(paymentProgress(-500, 500)).toEqual({pct: 100, tone: 'ok'})
  })

  it('is a sliver and orange against old debt', () => {
    // ฿500 funded against ฿20,500 owed — the mock's second state.
    const p = paymentProgress(-20500, 500)
    expect(p.tone).toBe('short')
    expect(p.pct).toBeCloseTo(2.4, 1)
  })

  it('is full when nothing is owed at all', () => {
    expect(paymentProgress(0, 0)).toEqual({pct: 100, tone: 'ok'})
    expect(paymentProgress(120, 0)).toEqual({pct: 100, tone: 'ok'})
  })

  it('never runs past 100% or below 0%', () => {
    expect(paymentProgress(-500, 4000).pct).toBe(100)
    expect(paymentProgress(-500, -200).pct).toBe(0)
  })
})

describe('paymentPillTone', () => {
  // The confirmed mock draws the money pill GREEN on the funded card and
  // ORANGE on the same card carrying ฿20,000 of old debt — both hold a
  // positive ฿500, so the pill follows the SHORTFALL, not the amount. The
  // shared target-progress rule cannot express that: a Payment envelope has
  // no target.
  it('is green when the card can be paid in full', () => {
    expect(paymentPillTone(500, 0)).toBe('is-green')
  })

  it('is orange while the card is still short', () => {
    expect(paymentPillTone(500, 20000)).toBe('is-orange')
  })

  it('is red when the envelope itself is overspent', () => {
    expect(paymentPillTone(-30, 0)).toBe('is-red')
    expect(paymentPillTone(-30, 200)).toBe('is-red')
  })

  it('is grey when there is neither money nor debt', () => {
    expect(paymentPillTone(0, 0)).toBe('is-zero')
  })
})
