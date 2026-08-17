// Shared by WritingHistoryPage and WritingEntryDetailPage.
//
// `entry.date` is a bare `YYYY-MM-DD` string. `new Date('YYYY-MM-DD')` parses
// that as UTC midnight (per the ECMA-262 date-time string format), which in
// Asia/Bangkok (+07) lands at 07:00 the same day and looks correct — but any
// viewer in a negative-UTC-offset zone sees the PREVIOUS day. Split the parts
// out and construct the Date from them instead, which is local-time by
// definition and always renders the intended calendar day.
export const formatDateThai = (isoDate: string): string => {
  const [year, month, day] = isoDate.split('-').map(Number)
  const localDate = new Date(year, month - 1, day)
  return localDate.toLocaleDateString('th-TH', { day: 'numeric', month: 'short', year: 'numeric' })
}
