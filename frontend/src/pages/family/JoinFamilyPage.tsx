import { useState } from 'react'
import { Navigate } from 'react-router-dom'
import { useCreateFamilyMutation } from '../../shared/api/api'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'

export function JoinFamilyPage() {
  const { displayName, familyId, isLoadingProfile } = useCurrentUser()
  const [inviteCode, setInviteCode] = useState('')
  const [showCreateForm, setShowCreateForm] = useState(false)
  const [familyName, setFamilyName] = useState('')

  // Once /api/me reports a family (either because the user already
  // belongs to one, or because Create Family just succeeded and RTK
  // Query refetched Me), navigate back into the app shell. Without
  // this, JoinFamilyPage stays mounted because FamilyRequiredRoute
  // only wraps the app shell, not /join-family.
  if (!isLoadingProfile && familyId) {
    return <Navigate to="/" replace />
  }

  const [createFamily, { isLoading: isCreating, error: createError }] = useCreateFamilyMutation()

  const handleCreate = async (event: React.FormEvent) => {
    event.preventDefault()
    const trimmed = familyName.trim()
    if (!trimmed) return

    try {
      await createFamily({ name: trimmed }).unwrap()
      // On success, RTK Query invalidates the Me tag, useCurrentUser
      // refetches, FamilyRequiredRoute sees a familyId, and the app
      // router navigates the user out of /join-family automatically.
    } catch {
      // The error is rendered below from `createError`.
    }
  }

  return (
    <section className="page page--join-family">
      <div className="card">
        <h1>ยินดีต้อนรับ{displayName ? `, ${displayName}` : ''}</h1>
        <p>คุณยังไม่ได้เข้าร่วม family</p>

        <div className="join-family__option">
          <label htmlFor="invite-code">มี invite code แล้ว?</label>
          <input
            id="invite-code"
            type="text"
            placeholder="XXXX-XXXX"
            value={inviteCode}
            onChange={(e) => setInviteCode(e.target.value)}
            disabled
          />
          <button type="button" className="btn btn--primary" disabled>
            Join (coming soon)
          </button>
        </div>

        <div className="divider">or</div>

        {!showCreateForm ? (
          <div className="join-family__option">
            <button
              type="button"
              className="btn btn--outline"
              onClick={() => setShowCreateForm(true)}
            >
              + Create a new family
            </button>
          </div>
        ) : (
          <form className="join-family__option" onSubmit={handleCreate}>
            <label htmlFor="family-name">Family name</label>
            <input
              id="family-name"
              type="text"
              placeholder="ครอบครัว..."
              value={familyName}
              onChange={(e) => setFamilyName(e.target.value)}
              autoFocus
              required
              maxLength={120}
              disabled={isCreating}
            />
            <div style={{ display: 'flex', gap: 8 }}>
              <button
                type="submit"
                className="btn btn--primary"
                disabled={isCreating || !familyName.trim()}
              >
                {isCreating ? 'Creating…' : 'Create'}
              </button>
              <button
                type="button"
                className="btn btn--outline"
                onClick={() => {
                  setShowCreateForm(false)
                  setFamilyName('')
                }}
                disabled={isCreating}
              >
                Cancel
              </button>
            </div>
            {createError && (
              <p style={{ color: 'var(--color-danger)', marginTop: 8 }}>
                Could not create family. Please try again.
              </p>
            )}
          </form>
        )}
      </div>
    </section>
  )
}
