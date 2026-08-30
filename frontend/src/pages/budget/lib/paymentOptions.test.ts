import {describe, expect, it} from 'vitest'
import type {BudgetAccountDto, EnvelopeDto, EnvelopeGroupDto} from '../../../shared/api/api'
import {
  fundingEnvelopeOptions,
  needsFundingEnvelope,
  payingAccountOptions,
  payingCardWarning,
} from './paymentOptions'

function acc(over: Partial<BudgetAccountDto> & {id: string; name: string}): BudgetAccountDto {
  return {
    type: 'Cash', balance: 0, sortOrder: 0, isClosed: false, shortfall: null, ...over,
  }
}

function env(over: Partial<EnvelopeDto> & {categoryId: string; name: string}): EnvelopeDto {
  return {
    emoji: null, sortOrder: 0, isHidden: false,
    assigned: 0, activity: 0, available: 0,
    targetType: 'None', targetAmount: null, targetDueDate: null, targetDayOfMonth: null,
    targetProgressFraction: null, targetHint: null, isEveryday: false,
    paymentForAccountId: null, shortfall: null, cardSpending: null,
    ...over,
  }
}

function group(categories: EnvelopeDto[]): EnvelopeGroupDto {
  return {
    groupId: 'g1', name: 'ค่ากิน', sortOrder: 0, isHidden: false,
    totalAssigned: 0, totalActivity: 0, totalAvailable: 0, categories,
  }
}

describe('payingAccountOptions', () => {
  const accounts = [
    acc({id: 'cash', name: 'เงินสด', balance: 10000}),
    acc({id: 'kbank', name: 'KBank', type: 'Credit', balance: -500, shortfall: 0}),
    acc({id: 'scb', name: 'SCB', type: 'Credit', balance: -2000, shortfall: 2000}),
    acc({id: 'car', name: 'รถ', type: 'Loan', balance: -300000}),
    acc({id: 'old', name: 'บัญชีเก่า', isClosed: true, balance: 0}),
  ]

  it('offers cash and other cards, with the balance as context', () => {
    expect(payingAccountOptions(accounts, 'kbank')).toEqual([
      {id: 'cash', label: 'เงินสด (฿10,000.00)'},
      {id: 'scb', label: 'SCB (−฿2,000.00)'},
    ])
  })

  // MakePaymentHandler: "A Loan account cannot be the paying account."
  it('never offers a Loan', () => {
    expect(payingAccountOptions(accounts, 'kbank').map(o => o.id)).not.toContain('car')
  })

  // Paying an account from itself is refused server-side; never offer it.
  it('never offers the account being paid', () => {
    expect(payingAccountOptions(accounts, 'cash').map(o => o.id)).not.toContain('cash')
  })

  it('never offers a closed account', () => {
    expect(payingAccountOptions(accounts, 'kbank').map(o => o.id)).not.toContain('old')
  })
})

describe('needsFundingEnvelope', () => {
  // menunest-214: required for a Loan, refused for a Credit card.
  it('is true for a Loan', () => {
    expect(needsFundingEnvelope('Loan')).toBe(true)
  })
  it('is false for a Credit card', () => {
    expect(needsFundingEnvelope('Credit')).toBe(false)
  })
})

describe('fundingEnvelopeOptions', () => {
  const groups = [
    group([
      env({categoryId: 'c1', name: 'ผ่อนรถ', emoji: '🚗', available: 8000}),
      env({categoryId: 'c2', name: 'ซ่อน', isHidden: true}),
    ]),
    {
      ...group([env({categoryId: 'p1', name: 'จ่ายบัตร KBank', paymentForAccountId: 'kbank'})]),
      groupId: 'g2',
      name: 'บัตรเครดิต',
    },
  ]

  it('offers ordinary envelopes with their available amount', () => {
    expect(fundingEnvelopeOptions(groups)).toEqual([
      {id: 'c1', label: '🚗 ผ่อนรถ (฿8,000.00)'},
    ])
  })

  // PaymentCategoryRule: "a Payment envelope cannot fund another debt's payment."
  it('never offers a Payment envelope', () => {
    expect(fundingEnvelopeOptions(groups).map(o => o.id)).not.toContain('p1')
  })

  it('never offers a hidden envelope', () => {
    expect(fundingEnvelopeOptions(groups).map(o => o.id)).not.toContain('c2')
  })
})

describe('payingCardWarning', () => {
  const card = acc({id: 'scb', name: 'SCB', type: 'Credit', balance: -2000})

  // Correction #4: paying a CARD with another card moves Ready to Assign UP.
  // Correct, but surprising — so the paying card's own shortfall is surfaced.
  // The outflow leg is uncategorised, so it never moves the paying card's
  // Available (PaymentEnvelopeMath.Available) — only its balance falls.
  it('names the paying card, its new shortfall, and the Ready-to-Assign rise', () => {
    const w = payingCardWarning(card, 500, 1000, 'Credit')
    expect(w).not.toBeNull()
    expect(w!.shortfallAfter).toBe(2500) // −(−2000 − 1000) − 500
    expect(w!.text).toContain('SCB')
    expect(w!.text).toContain('฿2,500.00')
    expect(w!.text).toContain('฿1,000.00')
  })

  it('floors the new shortfall at zero when the card is over-funded', () => {
    expect(payingCardWarning(card, 5000, 1000, 'Credit')!.shortfallAfter).toBe(0)
  })

  // Paying a LOAN is the mirror image, and the card-to-card reasoning above is
  // simply wrong for it. menunest-214 makes the outflow leg CATEGORISED, so by
  // PaymentEnvelopeMath.Available the paying card's own Payment envelope rises
  // by the same amount its balance falls — the shortfall does not move at all.
  it('leaves the shortfall unchanged when the card pays a Loan', () => {
    const w = payingCardWarning(card, 500, 1000, 'Loan')
    expect(w).not.toBeNull()
    expect(w!.shortfallAfter).toBe(1500) // −(−2000) − 500, the SAME as before
  })

  // Ready to Assign does not move either: the funding Envelope falls by the
  // amount while the Payment envelope rises by it, and no cash account is
  // touched. Claiming a rise here would be a plain falsehood on screen.
  it('never claims a Ready-to-Assign rise when the card pays a Loan', () => {
    const w = payingCardWarning(card, 500, 1000, 'Loan')
    expect(w!.text).not.toContain('เงินพร้อมจัดสรรจะเพิ่มขึ้น')
    expect(w!.text).toContain('SCB')
  })

  it('says nothing when paying from cash', () => {
    expect(payingCardWarning(acc({id: 'cash', name: 'เงินสด'}), 0, 1000, 'Credit')).toBeNull()
  })

  it('says nothing before an amount is typed', () => {
    expect(payingCardWarning(card, 0, null, 'Credit')).toBeNull()
    expect(payingCardWarning(card, 0, 0, 'Credit')).toBeNull()
  })

  it('says nothing when no paying account is picked yet', () => {
    expect(payingCardWarning(null, 0, 1000, 'Credit')).toBeNull()
  })
})
