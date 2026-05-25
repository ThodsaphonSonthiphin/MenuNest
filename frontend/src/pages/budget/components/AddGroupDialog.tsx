import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {TextBox} from '@syncfusion/react-inputs'
import {useCreateBudgetGroupMutation} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

interface FormValues { name: string }

export function AddGroupDialog({onClose}: {onClose: () => void}) {
  const [create, {isLoading}] = useCreateBudgetGroupMutation()
  const [err, setErr] = useState<string | null>(null)
  const {control, handleSubmit, formState} = useForm<FormValues>({defaultValues: {name: ''}})

  const onSubmit = handleSubmit(async values => {
    setErr(null)
    try {
      await create({name: values.name.trim()}).unwrap()
      onClose()
    } catch (e) {
      setErr(getErrorMessage(e))
    }
  })

  return (
    <div
      className="budget-modal-overlay"
      onClick={(e) => { if (e.target === e.currentTarget) onClose() }}
      data-testid="bdg-add-group-dialog"
    >
      <form className="budget-modal" onSubmit={onSubmit} noValidate>
        <h3>Add Group</h3>
        <div className="subtitle">A group bundles related envelopes (e.g. "Bills", "Fun").</div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Name</div>
          <Controller
            control={control}
            name="name"
            rules={{
              required: 'Name is required.',
              maxLength: {value: 120, message: 'Max 120 characters.'},
              validate: v => v.trim().length > 0 || 'Name is required.',
            }}
            render={({field}) => (
              <TextBox
                value={field.value}
                placeholder="e.g. Bills"
                onChange={e => field.onChange(e.value ?? '')}
              />
            )}
          />
          {formState.errors.name && <p className="field-error">{formState.errors.name.message}</p>}
        </div>

        {err && <p className="field-error">{err}</p>}

        <div className="budget-modal-footer">
          <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={isLoading}>
            {isLoading ? '…' : 'Create'}
          </Button>
        </div>
      </form>
    </div>
  )
}
