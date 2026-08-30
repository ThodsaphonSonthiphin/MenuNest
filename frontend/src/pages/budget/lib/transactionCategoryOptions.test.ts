import {describe, expect, it} from 'vitest'
import type {EnvelopeDto, EnvelopeGroupDto} from '../../../shared/api/api'
import {transactionCategoryOptions} from './transactionCategoryOptions'

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

const groups: EnvelopeGroupDto[] = [
  {
    groupId: 'g1', name: 'ค่ากิน', sortOrder: 0, isHidden: false,
    totalAssigned: 0, totalActivity: 0, totalAvailable: 0,
    categories: [
      env({categoryId: 'c1', name: 'อาหาร', emoji: '🍜'}),
      env({categoryId: 'c2', name: 'ค่าไฟ'}),
    ],
  },
  {
    groupId: 'g2', name: 'บัตรเครดิต', sortOrder: 1, isHidden: false,
    totalAssigned: 0, totalActivity: 0, totalAvailable: 0,
    categories: [
      env({categoryId: 'p1', name: 'จ่ายบัตร KBank', emoji: '💳', paymentForAccountId: 'acct-kbank'}),
    ],
  },
]

describe('transactionCategoryOptions', () => {
  it('leads with Uncategorized, then every ordinary envelope', () => {
    expect(transactionCategoryOptions(groups, '__uncategorized__')).toEqual([
      {id: '__uncategorized__', label: '— Uncategorized —'},
      {id: 'c1', label: '🍜 อาหาร'},
      {id: 'c2', label: '• ค่าไฟ'},
    ])
  })

  // menunest-203, and the whole reason this module exists: categorising an
  // ordinary transaction to a Payment envelope makes the money vanish —
  // ฿500 off a cash account, recorded by no envelope, RTA down by ฿500.
  // The backend now refuses it (OrdinaryEnvelopeRule); the picker must never
  // offer it, so the refusal is never reached by an honest user.
  it('never offers a Payment envelope', () => {
    expect(transactionCategoryOptions(groups, '__uncategorized__').map(o => o.id))
      .not.toContain('p1')
  })

  it('offers only Uncategorized when every envelope is a Payment envelope', () => {
    expect(transactionCategoryOptions([groups[1]], '__uncategorized__')).toEqual([
      {id: '__uncategorized__', label: '— Uncategorized —'},
    ])
  })
})
