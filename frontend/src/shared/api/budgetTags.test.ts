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
        expect(budgetWriteTagsAllMonths).toEqual(['BudgetSummary', 'BudgetHistory'])
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

    it.each(writeEndpoints)('%s invalidates BudgetHistory', (endpoint) => {
        const start = source.indexOf(`${endpoint}: build.mutation`)
        expect(start, `${endpoint} is no longer a mutation in api.ts`).toBeGreaterThan(-1)

        // The endpoint block runs to the NEXT endpoint definition. Do not stop
        // at the first `}),` — that one closes the `query:` object literal, and
        // slicing there cuts the block off before invalidatesTags is reached.
        const rest = source.slice(start)
        const next = rest.slice(1).search(/\n\s+\w+: build\.(mutation|query)/)
        const block = rest.slice(0, next === -1 ? 600 : next + 1)

        expect(block, `${endpoint} must invalidate Change history, not only the summary`).toMatch(
            /budgetWriteTags(AllMonths)?/,
        )
    })

    it('no budget mutation invalidates BudgetSummary alone', () => {
        const summaryOnly = /invalidatesTags:\s*(\(_r, _e, a\) =>\s*)?\[\s*(\{type: )?'?BudgetSummary'?[^\]]*\]/g
        const offenders = [...source.matchAll(summaryOnly)]
            .map((m) => m[0])
            .filter((m) => !m.includes('BudgetHistory'))
        expect(offenders).toEqual([])
    })
})
