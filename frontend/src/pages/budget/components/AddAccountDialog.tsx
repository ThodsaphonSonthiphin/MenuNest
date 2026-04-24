import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {DropDownList} from '@syncfusion/react-dropdowns'
import {NumericTextBox, TextBox} from '@syncfusion/react-inputs'
import {
  useCreateBudgetAccountMutation,
  type BudgetAccountType,
} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

interface FormValues {
  name: string
  type: BudgetAccountType
  openingBalance: number | null
}

const ACCOUNT_TYPE_OPTIONS: {id: BudgetAccountType; label: string}[] = [
  {id: 'Cash',   label: 'Cash'},
  {id: 'Credit', label: 'Credit'},
  {id: 'Loan',   label: 'Loan'},
]

/**
 * Add Account dialog — creates a new `BudgetAccount` with an opening balance.
 * The `Closed` account type is intentionally not offered here; existing
 * accounts are flipped to closed via `updateBudgetAccount`.
 */
export function AddAccountDialog({onClose}: {onClose: () => void}) {
  const [createAccount, {isLoading}] = useCreateBudgetAccountMutation()
  const [err, setErr] = useState<string | null>(null)
  const {control, handleSubmit, formState} = useForm<FormValues>({
    defaultValues: {name: '', type: 'Cash', openingBalance: 0},
  })

  const onSubmit = handleSubmit(async values => {
    setErr(null)
    try {
      await createAccount({
        name: values.name.trim(),
        type: values.type,
        openingBalance: Number(values.openingBalance ?? 0),
        sortOrder: 0,
      }).unwrap()
      onClose()
    } catch (e) {
      setErr(getErrorMessage(e))
    }
  })

  return (
    <div
      className="budget-modal-overlay"
      onClick={e => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <form className="budget-modal" onSubmit={onSubmit} noValidate>
        <h3>Add Account</h3>
        <div className="subtitle">Track cash, credit, or loan balances.</div>

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
                placeholder="e.g. Kasikorn Checking"
                onChange={e => field.onChange(e.value ?? '')}
              />
            )}
          />
          {formState.errors.name && (
            <p className="field-error">{formState.errors.name.message}</p>
          )}
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Type</div>
          <Controller
            control={control}
            name="type"
            rules={{required: 'Pick an account type.'}}
            render={({field}) => (
              <DropDownList
                dataSource={ACCOUNT_TYPE_OPTIONS}
                fields={{text: 'label', value: 'id'}}
                value={field.value || null}
                onChange={(e: {value: unknown}) =>
                  field.onChange((e.value as BudgetAccountType) ?? 'Cash')
                }
              />
            )}
          />
          {formState.errors.type && (
            <p className="field-error">{formState.errors.type.message}</p>
          )}
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Opening balance</div>
          <Controller
            control={control}
            name="openingBalance"
            render={({field}) => (
              <NumericTextBox
                value={field.value ?? 0}
                onChange={e => field.onChange((e.value as number | null) ?? 0)}
              />
            )}
          />
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
