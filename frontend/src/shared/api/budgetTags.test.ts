import {readFileSync} from 'node:fs'
import {fileURLToPath} from 'node:url'
import {describe, expect, it} from 'vitest'

import {budgetWriteTags, budgetWriteTagsAllMonths} from './budgetTags'

describe('budgetWriteTags', () => {
    it('invalidates the summary AND the history for that month', () => {
        expect(budgetWriteTags({year: 2026, month: 8})).toEqual([
            {type: 'BudgetSummary', id: '2026-8'},
            {type: 'BudgetHistory', id: '2026-8'},
        ])
    })

    // The id has to be byte-identical to the one `listBudgetHistory` hands to
    // `providesTags` — `${a.year}-${a.month}`. An id of '2026-08' would look
    // right in a diff and silently invalidate nothing, which is the same
    // outcome as #109 with none of its visibility.
    it('builds the id un-padded, the way the history query provides it', () => {
        const [, history] = budgetWriteTags({year: 2026, month: 8})
        expect(history).toEqual({type: 'BudgetHistory', id: '2026-8'})
        expect(budgetWriteTags({year: 2026, month: 12})[1]).toEqual({
            type: 'BudgetHistory',
            id: '2026-12',
        })
    })

    it('invalidates both types wholesale when the write carries no month', () => {
        expect(budgetWriteTagsAllMonths()).toEqual(['BudgetSummary', 'BudgetHistory'])
    })

    it('hands every caller its own array rather than one shared instance', () => {
        expect(budgetWriteTagsAllMonths()).not.toBe(budgetWriteTagsAllMonths())
        expect(budgetWriteTags({year: 2026, month: 8})).not.toBe(
            budgetWriteTags({year: 2026, month: 8}),
        )
    })
})

// #109 shipped because four separate endpoints each invalidated 'BudgetSummary'
// and none invalidated 'BudgetHistory'. A correct helper does not prevent a
// FIFTH endpoint repeating the omission, and nothing in the type system will
// object — so the wiring itself is asserted here, at the source.
describe('every budget write endpoint is wired to the helper', () => {
    const source = readFileSync(fileURLToPath(new URL('./api.ts', import.meta.url)), 'utf8')

    // Each of these backend handlers calls BudgetChangeRecorder.Record, so each
    // one leaves a Change history row that the cached list would not know about.
    const writeEndpoints = [
        'setAssignedAmount',
        'moveMoney',
        'coverOverspending',
        'setEverydayMarks',
    ]

    // These record no row, but they change what the history list RENDERS: the
    // envelope's name, and whether it still exists at all. Missing them is the
    // same staleness one seam over — the failure #109's first fix did not cover,
    // because it enumerated only the endpoints that WRITE rows.
    //
    // deleteBudgetGroup is deliberately absent: DeleteGroupHandler refuses while
    // the group still has categories, so it can never affect a history row.
    const envelopeShapeEndpoints = ['updateBudgetCategory', 'deleteBudgetCategory']

    // menunest-204/209 (#112). These record NO Change history row — spec §7:
    // a payment is two Budget transactions, so it is fixed where transactions
    // are fixed, and the Shortcut rail keeps exactly its three slots. They use
    // the helper for its OTHER half: a Payment envelope's Available is
    // cumulative (§4.2), so a payment dated in one month moves every later
    // month's derived shortfall, and only the month-independent variant covers
    // that. Listed separately from `writeEndpoints` so the distinction between
    // "records history" and "merely invalidates it" stays visible.
    const paymentEndpoints = ['makePayment', 'updatePayment', 'deletePayment']

    /** The source text of one endpoint definition, up to the next one. */
    function endpointBlock(endpoint: string): string {
        const start = source.indexOf(`${endpoint}: build.mutation`)
        expect(start, `${endpoint} is no longer a mutation in api.ts`).toBeGreaterThan(-1)

        // The block runs to the NEXT endpoint definition. Do not stop at the
        // first `}),` — that one closes the `query:` object literal, and slicing
        // there cuts the block off before invalidatesTags is reached.
        const rest = source.slice(start)
        const next = rest.slice(1).search(/\n\s+\w+: build\.(mutation|query)/)
        return rest.slice(0, next === -1 ? 600 : next + 1)
    }

    it.each(writeEndpoints)('%s invalidates BudgetHistory via the helper', (endpoint) => {
        expect(
            endpointBlock(endpoint),
            `${endpoint} must invalidate Change history, not only the summary`,
        ).toMatch(/budgetWriteTags(AllMonths)?/)
    })

    it.each(envelopeShapeEndpoints)('%s invalidates BudgetHistory', (endpoint) => {
        expect(
            endpointBlock(endpoint),
            `${endpoint} changes what a history row renders, so it must invalidate BudgetHistory`,
        ).toContain('BudgetHistory')
    })

    // Pins the SET, not just each member. Adding a fifth caller of the helper —
    // or quietly dropping one — then has to be a deliberate act with a reason,
    // rather than something a diff can slide past.
    //
    // This replaces an earlier assertion named "no budget mutation invalidates
    // BudgetSummary alone", which was deleted for being both vacuous and false:
    // its regex only matched arrays whose FIRST element was BudgetSummary, so it
    // never examined `['BudgetGroups', 'BudgetSummary']`; and the rule it named
    // is wrong anyway, because createBudgetGroup and createBudgetCategory
    // legitimately invalidate the summary without touching history.
    it.each(paymentEndpoints)('%s invalidates every month via the helper', (endpoint) => {
        expect(
            endpointBlock(endpoint),
            `${endpoint} moves a Payment envelope's cumulative Available, so every ` +
            `cached month goes stale — not just the payment's own`,
        ).toMatch(/budgetWriteTagsAllMonths/)
    })

    it('exactly the write and payment endpoints use the helper', () => {
        const callers = source
            .split('\n')
            .filter((l) => l.includes('invalidatesTags') && l.includes('budgetWriteTags'))
        expect(callers).toHaveLength(writeEndpoints.length + paymentEndpoints.length)
    })
})
