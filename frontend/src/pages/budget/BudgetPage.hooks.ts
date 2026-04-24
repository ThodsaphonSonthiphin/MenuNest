import {useEffect, useState} from 'react'
import {useAppDispatch, useAppSelector} from '../../store'
import {useGetBudgetSummaryQuery} from '../../shared/api/api'
import {goPrevMonth, goNextMonth, setMonth} from './budgetSlice'

export type BudgetLayout = 'mobile' | 'tablet' | 'desktop'

export function useBudgetLayout(): BudgetLayout {
  const [w, setW] = useState<number>(() => window.innerWidth)
  useEffect(() => {
    const onResize = () => setW(window.innerWidth)
    window.addEventListener('resize', onResize)
    return () => window.removeEventListener('resize', onResize)
  }, [])
  if (w < 768) return 'mobile'
  if (w < 1280) return 'tablet'
  return 'desktop'
}

export function useBudgetData() {
  const {year, month} = useAppSelector(s => s.budget)
  const dispatch = useAppDispatch()
  const q = useGetBudgetSummaryQuery({year, month})
  return {
    year, month,
    summary: q.data, isLoading: q.isLoading, error: q.error,
    prev: () => dispatch(goPrevMonth()),
    next: () => dispatch(goNextMonth()),
    jump: (y: number, m: number) => dispatch(setMonth({year: y, month: m})),
  }
}

export function formatTHB(n: number): string {
  const sign = n < 0 ? '−' : ''
  return `${sign}฿${Math.abs(n).toLocaleString('en-US', {minimumFractionDigits: 2, maximumFractionDigits: 2})}`
}
