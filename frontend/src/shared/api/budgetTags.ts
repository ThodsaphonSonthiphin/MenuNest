import type {TagDescription} from '@reduxjs/toolkit/query'

/** The two tags every budget write makes stale. */
type BudgetWriteTag = TagDescription<'BudgetSummary' | 'BudgetHistory'>

/**
 * The tags a budget WRITE invalidates.
 *
 * A budget write does two things at once: it moves money, and it records a row
 * in **Change history** (menunest-193). So `BudgetSummary` and `BudgetHistory`
 * always go stale together — and holding the pair in ONE place is the point of
 * this module, because the second half is the half that gets forgotten.
 *
 * It was forgotten, on all four write mutations, and it shipped (#109): the row
 * reached the database, but the history query kept serving the empty list it had
 * cached, so the shortcut rail's Undo stayed greyed out until a full page reload.
 * `BudgetPage` holds a live subscription to that query, so the stale entry never
 * expired on its own and reopening the sheet could not clear it.
 *
 * Nothing in the type system connects a handler that writes a row to the query
 * that reads it. This function is that connection, made explicit.
 */
export function budgetWriteTags(arg: {year: number; month: number}): BudgetWriteTag[] {
    const id = `${arg.year}-${arg.month}`
    return [
        {type: 'BudgetSummary', id},
        {type: 'BudgetHistory', id},
    ]
}

/**
 * The month-independent variant, for a write whose request carries no
 * year/month — `setEverydayMarks`, whose mark applies to the envelope rather
 * than to one month.
 *
 * Invalidating the whole type is deliberately blunt: with no month in the
 * request there is no way to know which cached months the write touched, and a
 * stale Undo button is a worse outcome than an extra refetch.
 *
 * A function, not a constant, for the same reason `budgetWriteTags` is: every
 * caller gets its own array rather than sharing one instance with the store.
 */
export function budgetWriteTagsAllMonths(): BudgetWriteTag[] {
    return ['BudgetSummary', 'BudgetHistory']
}
