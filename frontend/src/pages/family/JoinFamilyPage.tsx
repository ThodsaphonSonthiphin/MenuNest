import { useState } from 'react'
import { Navigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { useCreateFamilyMutation } from '../../shared/api/api'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'

interface CreateFamilyForm {
  name: string
}

export function JoinFamilyPage() {
  const { displayName, familyId, isLoadingProfile } = useCurrentUser()
  const [showCreateForm, setShowCreateForm] = useState(false)

  const [createFamily, { isLoading: isCreating, error: createError }] = useCreateFamilyMutation()

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<CreateFamilyForm>({ defaultValues: { name: '' } })

  // Once /api/me reports a family (either because the user already
  // belongs to one, or because Create Family just succeeded and RTK
  // Query refetched Me), navigate back into the app shell.
  if (!isLoadingProfile && familyId) {
    return <Navigate to="/" replace />
  }

  const onSubmit = handleSubmit(async (values) => {
    try {
      await createFamily({ name: values.name.trim() }).unwrap()
    } catch {
      // surfaced below from createError
    }
  })

  const closeForm = () => {
    setShowCreateForm(false)
    reset()
  }

  return (
    <section className="page page--join-family">
      <div className="card">
        <h1>ยินดีต้อนรับ{displayName ? `, ${displayName}` : ''}</h1>
        <p>คุณยังไม่ได้เข้าร่วม family</p>

        <div className="join-family__option">
          <label htmlFor="invite-code">มี invite code แล้ว?</label>
          <input id="invite-code" type="text" placeholder="XXXX-XXXX" disabled />
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
          <form className="join-family__option" onSubmit={onSubmit} noValidate>
            <label htmlFor="family-name">
              Family name <span className="field-required">*</span>
            </label>
            <input
              id="family-name"
              type="text"
              placeholder="ครอบครัว..."
              autoFocus
              disabled={isCreating}
              aria-invalid={errors.name ? 'true' : 'false'}
              {...register('name', {
                required: 'Family name is required.',
                maxLength: { value: 120, message: 'Maximum 120 characters.' },
                validate: (v) => v.trim().length > 0 || 'Family name is required.',
              })}
            />
            {errors.name && <p className="field-error">{errors.name.message}</p>}

            <div style={{ display: 'flex', gap: 8 }}>
              <button type="submit" className="btn btn--primary" disabled={isCreating}>
                {isCreating ? 'Creating…' : 'Create'}
              </button>
              <button
                type="button"
                className="btn btn--outline"
                onClick={closeForm}
                disabled={isCreating}
              >
                Cancel
              </button>
            </div>
            {createError && (
              <p className="field-error" style={{ marginTop: 8 }}>
                Could not create family. Please try again.
              </p>
            )}
          </form>
        )}
      </div>
    </section>
  )
}
