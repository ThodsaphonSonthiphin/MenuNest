import {describe, it, expect} from 'vitest'
import type {EnvelopeDto, EnvelopeGroupDto} from '../../../shared/api/api'
import {coverSourceOptions, toFromCategoryId, READY_TO_ASSIGN} from './coverSourceOptions'

function cat(over: Partial<EnvelopeDto> & {categoryId: string}): EnvelopeDto {
  return {
    name: 'Cat', emoji: null, sortOrder: 0, isHidden: false,
    assigned: 0, activity: 0, available: 0,
    targetType: 'None', targetAmount: null, targetDueDate: null, targetDayOfMonth: null,
    targetProgressFraction: null, targetHint: null, isEveryday: false,
    paymentForAccountId: null, shortfall: null, cardSpending: null,
    ...over,
  } as EnvelopeDto
}

function group(categories: EnvelopeDto[]): EnvelopeGroupDto[] {
  return [{
    groupId: 'g1', name: 'Bills', sortOrder: 0, isHidden: false,
    totalAssigned: 0, totalActivity: 0, totalAvailable: 0, categories,
  } as EnvelopeGroupDto]
}

// The exact state from issue #115's screenshot: ค่าซักผ้า overspent by ฿110
// with ฿893.81 still to place.
const laundry = cat({categoryId: 'laundry', name: 'ค่าซักผ้า', emoji: '🧺', available: -110})
const food = cat({categoryId: 'food', name: 'อาหาร', emoji: '🍜', available: 240})
const rent = cat({categoryId: 'rent', name: 'ค่าเช่า', emoji: '🏠', available: 0})

describe('coverSourceOptions', () => {
  it('offers Ready to Assign first, with its amount, when money is still to place', () => {
    const options = coverSourceOptions(group([laundry, food, rent]), laundry, 893.81)

    expect(options[0]).toEqual({
      id: READY_TO_ASSIGN,
      label: '💰 เงินที่ยังไม่ได้จัดสรร (฿893.81)',
    })
    expect(options.map(o => o.id)).toEqual([READY_TO_ASSIGN, 'food'])
  })

  it('omits Ready to Assign when every baht already has a job', () => {
    const options = coverSourceOptions(group([laundry, food]), laundry, 0)
    expect(options.map(o => o.id)).toEqual(['food'])
  })

  it('omits Ready to Assign when it is overdrawn — it is no one\'s source', () => {
    const options = coverSourceOptions(group([laundry, food]), laundry, -500)
    expect(options.map(o => o.id)).toEqual(['food'])
  })

  it('is Ready to Assign alone when no envelope has cash to spare', () => {
    const options = coverSourceOptions(group([laundry, rent]), laundry, 893.81)
    expect(options.map(o => o.id)).toEqual([READY_TO_ASSIGN])
  })

  it('never offers the overspent envelope itself, which the server refuses', () => {
    const spare = cat({categoryId: 'spare', name: 'Spare', available: 50})
    // An overspent envelope cannot have available > 0, but the guard must not
    // depend on that — CoverOverspendingValidator refuses a self-cover outright.
    const options = coverSourceOptions(group([spare]), spare, 0)
    expect(options).toEqual([])
  })

  it('falls back to a bullet for an envelope with no emoji', () => {
    const plain = cat({categoryId: 'plain', name: 'Plain', emoji: null, available: 12.5})
    const options = coverSourceOptions(group([plain]), laundry, 0)
    expect(options[0].label).toBe('• Plain (฿12.50)')
  })
})

describe('toFromCategoryId', () => {
  it('sends null for Ready to Assign, so the server increments one envelope alone', () => {
    expect(toFromCategoryId(READY_TO_ASSIGN)).toBeNull()
  })

  it('passes a real envelope id straight through', () => {
    expect(toFromCategoryId('food')).toBe('food')
  })
})
