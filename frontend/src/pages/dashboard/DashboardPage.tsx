import { useCurrentUser } from '../../shared/hooks/useCurrentUser'

export function DashboardPage() {
  const { displayName } = useCurrentUser()

  return (
    <section className="page page--dashboard">
      <h1>สวัสดี, {displayName || 'there'}</h1>
      <p>Today's meals and quick stats go here.</p>
    </section>
  )
}
