import {useEffect, useRef, useState} from 'react'
import {useAppSelector} from '../../../store'
import {
  useListBudgetAccountTransactionsQuery,
  type BudgetTransactionDto,
} from '../../../shared/api/api'

const PAGE_SIZE = 50

/**
 * Drive the AccountDetailPage. Loads one page from the server; an
 * IntersectionObserver on a sentinel triggers `setSkip(prev + PAGE_SIZE)`
 * which the underlying query refetches and we merge into `allItems`.
 *
 * We accumulate locally rather than building one big merged query so
 * RTK Query keeps each page cached independently and re-uses entries
 * across navigations.
 */
export function useAccountDetail(accountId: string) {
  const {year, month} = useAppSelector(s => s.budget)
  const [skip, setSkip] = useState(0)
  const [allItems, setAllItems] = useState<BudgetTransactionDto[]>([])

  const {data, isLoading, isFetching, error} =
    useListBudgetAccountTransactionsQuery({accountId, year, month, skip, take: PAGE_SIZE})

  // Reset the accumulator when accountId or month changes.
  useEffect(() => {
    setSkip(0)
    setAllItems([])
  }, [accountId, year, month])

  // Append the newly-fetched page when it arrives.
  useEffect(() => {
    if (!data) return
    setAllItems(prev => {
      // If skip === 0 it's a fresh load (e.g. month change); replace.
      if (skip === 0) return data.items
      // Otherwise append, deduplicating by id (cache-invalidation races).
      const seen = new Set(prev.map(t => t.id))
      const fresh = data.items.filter(t => !seen.has(t.id))
      return [...prev, ...fresh]
    })
  }, [data, skip])

  const endSentinelRef = useRef<HTMLDivElement | null>(null)
  const hasMore = data?.hasMore ?? false

  useEffect(() => {
    const node = endSentinelRef.current
    if (!node || !hasMore || isFetching) return
    const io = new IntersectionObserver((entries) => {
      const entry = entries[0]
      if (entry?.isIntersecting) {
        setSkip(prev => prev + PAGE_SIZE)
      }
    }, {rootMargin: '120px'})
    io.observe(node)
    return () => io.disconnect()
  }, [hasMore, isFetching])

  return {
    account: data?.account ?? null,
    items: allItems,
    isLoading,
    isFetching,
    error,
    endSentinelRef,
    hasMore,
  }
}
