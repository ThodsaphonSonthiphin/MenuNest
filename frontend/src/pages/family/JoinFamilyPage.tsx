import { useState } from 'react'
import { Navigate } from 'react-router-dom'
import { Controller, useForm } from 'react-hook-form'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { TextBox } from '@syncfusion/react-inputs'
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
    control,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<CreateFamilyForm>({ defaultValues: { name: '' } })

  if (!isLoadingProfile && familyId) {
    return <Navigate to="/" replace />
  }

  const onSubmit = handleSubmit(async (values) => {
    try {
      await createFamily({ name: values.name.trim() }).unwrap()
    } catch {
      // surfaced below via createError
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
          <label>มี invite code แล้ว?</label>
          <TextBox placeholder="XXXX-XXXX" disabled />
          <Button variant={Variant.Filled} color={Color.Primary} disabled>
            Join (coming soon)
          </Button>
        </div>

        <div className="divider">or</div>

        {!showCreateForm ? (
          <div className="join-family__option">
            <Button
              variant={Variant.Outlined}
              color={Color.Primary}
              onClick={() => setShowCreateForm(true)}
            >
              + Create a new family
            </Button>
          </div>
        ) : (
          <form className="join-family__option" onSubmit={onSubmit} noValidate>
            <label htmlFor="family-name">
              Family name <span className="field-required">*</span>
            </label>
            <Controller
              control={control}
              name="name"
              rules={{
                required: 'Family name is required.',
                maxLength: { value: 120, message: 'Maximum 120 characters.' },
                validate: (v) => v.trim().length > 0 || 'Family name is required.',
              }}
              render={({ field, fieldState }) => (
                <TextBox
                  id="family-name"
                  placeholder="ครอบครัว..."
                  autoFocus
                  disabled={isCreating}
                  value={field.value}
                  onChange={(e) => field.onChange(e.value ?? '')}
                  color={fieldState.error ? ('Error' as never) : undefined}
                />
              )}
            />
            {errors.name && <p className="field-error">{errors.name.message}</p>}

            <div style={{ display: 'flex', gap: 8 }}>
              <Button
                type="submit"
                variant={Variant.Filled}
                color={Color.Primary}
                disabled={isCreating}
              >
                {isCreating ? 'Creating…' : 'Create'}
              </Button>
              <Button
                type="button"
                variant={Variant.Outlined}
                color={Color.Secondary}
                onClick={closeForm}
                disabled={isCreating}
              >
                Cancel
              </Button>
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
