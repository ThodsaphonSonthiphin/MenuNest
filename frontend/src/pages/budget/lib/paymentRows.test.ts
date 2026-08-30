import {describe, expect, it} from 'vitest'
import type {BudgetTransactionDto} from '../../../shared/api/api'
import {
  groupPaymentLegs, paymentDraftFromRow, paymentRowLabel, type PaymentTxRow,
} from './paymentRows'

function tx(over: Partial<BudgetTransactionDto> & {id: string}): BudgetTransactionDto {
  return {
    accountId: 'a', accountName: 'เงินสด',
    categoryId: null, categoryName: null, categoryEmoji: null,
    amount: -100, date: '2026-08-10', notes: null,
    createdByUserId: 'u', createdByDisplayName: 'Nok',
    paymentId: null,
    ...over,
  }
}

const outLeg = tx({
  id: 'out', accountId: 'cash', accountName: 'เงินสด',
  amount: -500, paymentId: 'P1', date: '2026-08-12',
})
const inLeg = tx({
  id: 'in', accountId: 'kbank', accountName: 'KBank',
  amount: 500, paymentId: 'P1', date: '2026-08-12',
})

describe('groupPaymentLegs', () => {
  it('leaves ordinary transactions alone, in order', () => {
    const a = tx({id: 'a'})
    const b = tx({id: 'b'})
    const rows = groupPaymentLegs([a, b])
    expect(rows).toHaveLength(2)
    expect(rows.map(r => r.kind)).toEqual(['transaction', 'transaction'])
    expect(rows.map(r => r.key)).toEqual(['a', 'b'])
  })

  // Correction #3 / menunest-209: the two legs are ONE payment to the user.
  // Rendered as two rows they both refuse edit and delete — a dead end.
  it('collapses the two legs of a payment into one row', () => {
    const rows = groupPaymentLegs([outLeg, inLeg])
    expect(rows).toHaveLength(1)
    const row = rows[0] as PaymentTxRow
    expect(row.kind).toBe('payment')
    expect(row.paymentId).toBe('P1')
    expect(row.legs.map(l => l.id)).toEqual(['out', 'in'])
    expect(row.complete).toBe(true)
  })

  it('keeps the payment where its first leg was, among ordinary rows', () => {
    const a = tx({id: 'a'})
    const z = tx({id: 'z'})
    const rows = groupPaymentLegs([a, outLeg, z, inLeg])
    expect(rows.map(r => r.key)).toEqual(['a', 'P1', 'z'])
  })

  it('reads the magnitude, the paying leg and the paid leg off the pair', () => {
    const row = groupPaymentLegs([outLeg, inLeg])[0] as PaymentTxRow
    expect(row.amount).toBe(500)
    expect(row.fromLeg?.accountName).toBe('เงินสด')
    expect(row.toLeg?.accountName).toBe('KBank')
    expect(row.date).toBe('2026-08-12')
  })

  // An account-detail feed is filtered to ONE account, so it only ever sees
  // one leg of a payment. It must still render as a payment, not as an
  // ordinary row that silently refuses every edit.
  it('still makes a payment row from a lone leg, marked incomplete', () => {
    const rows = groupPaymentLegs([inLeg])
    expect(rows).toHaveLength(1)
    const row = rows[0] as PaymentTxRow
    expect(row.kind).toBe('payment')
    expect(row.complete).toBe(false)
    expect(row.fromLeg).toBeNull()
    expect(row.toLeg?.id).toBe('in')
    expect(row.amount).toBe(500)
  })

  it('reads the magnitude off a lone outflow leg too', () => {
    const row = groupPaymentLegs([outLeg])[0] as PaymentTxRow
    expect(row.amount).toBe(500)
    expect(row.toLeg).toBeNull()
    expect(row.fromLeg?.id).toBe('out')
  })

  it('keeps two different payments apart', () => {
    const other = tx({id: 'o1', amount: -300, paymentId: 'P2'})
    const rows = groupPaymentLegs([outLeg, other, inLeg])
    expect(rows.map(r => r.key)).toEqual(['P1', 'P2'])
  })
})

describe('paymentRowLabel', () => {
  it('names the action and the account being paid', () => {
    const row = groupPaymentLegs([outLeg, inLeg])[0] as PaymentTxRow
    expect(paymentRowLabel(row, 'Credit')).toEqual({
      title: 'จ่ายบัตร KBank',
      subtitle: 'Payment · เงินสด → KBank',
    })
  })

  it('uses the loan word when the account being paid is a Loan', () => {
    const row = groupPaymentLegs([outLeg, inLeg])[0] as PaymentTxRow
    expect(paymentRowLabel(row, 'Loan').title).toBe('จ่ายค่างวด KBank')
  })

  it('prefers the note the user wrote as the title', () => {
    const row = groupPaymentLegs([
      {...outLeg, notes: 'บิลรอบ ส.ค.'},
      {...inLeg, notes: 'บิลรอบ ส.ค.'},
    ])[0] as PaymentTxRow
    expect(paymentRowLabel(row, 'Credit').title).toBe('บิลรอบ ส.ค.')
  })

  it('says where the other half is when only one leg is visible', () => {
    const row = groupPaymentLegs([inLeg])[0] as PaymentTxRow
    const label = paymentRowLabel(row, 'Credit')
    expect(label.title).toBe('จ่ายบัตร KBank')
    expect(label.subtitle).toBe('Payment · other half is on the paying account')
  })

  // menunest-212's closing line: "Avoid, per CONTEXT.md: จ่ายหนี้, ชำระ,
  // transfer, pay off, settle." This is NOT a rare fallback — on any Cash
  // account's detail page every card payment's outflow leg lands here — so the
  // neutral word has to be clean, not merely short.
  it('does not guess the action word when the account being paid is unknown', () => {
    const row = groupPaymentLegs([outLeg])[0] as PaymentTxRow
    expect(paymentRowLabel(row, null).title).toBe('การจ่าย')
  })

  it('uses none of menunest-212\'s avoided words in the neutral fallback', () => {
    const row = groupPaymentLegs([outLeg])[0] as PaymentTxRow
    const {title, subtitle} = paymentRowLabel(row, null)
    for (const banned of ['จ่ายหนี้', 'ชำระ']) {
      expect(title).not.toContain(banned)
      expect(subtitle).not.toContain(banned)
    }
  })
})

describe('paymentDraftFromRow', () => {
  // menunest-214: ONLY the outflow leg ever carries a category (the Envelope
  // funding a loan instalment). Reading it off the wrong leg would send null
  // for every loan edit, which the API refuses — and nothing else in the app
  // would notice, so the rule is pinned here rather than left inline.
  it('reads the funding envelope off the OUTFLOW leg', () => {
    const row = groupPaymentLegs([
      {...outLeg, categoryId: 'cat-car'},
      inLeg,
    ])[0] as PaymentTxRow
    expect(paymentDraftFromRow(row)).toEqual({
      paymentId: 'P1',
      fromAccountId: 'cash',
      toAccountId: 'kbank',
      amount: 500,
      date: '2026-08-12',
      notes: null,
      categoryId: 'cat-car',
    })
  })

  it('carries a null category for an uncategorised card payment', () => {
    const row = groupPaymentLegs([outLeg, inLeg])[0] as PaymentTxRow
    expect(paymentDraftFromRow(row)?.categoryId).toBeNull()
  })

  // An account-detail feed only ever holds one leg, so it can never build a
  // draft — the edit path must stay closed there rather than send half a payment.
  it('refuses to build a draft from a lone leg', () => {
    expect(paymentDraftFromRow(groupPaymentLegs([inLeg])[0] as PaymentTxRow)).toBeNull()
    expect(paymentDraftFromRow(groupPaymentLegs([outLeg])[0] as PaymentTxRow)).toBeNull()
  })
})
