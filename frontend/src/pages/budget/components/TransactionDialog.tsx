import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {DropDownList} from '@syncfusion/react-dropdowns'
import {NumericTextBox, TextArea} from '@syncfusion/react-inputs'
import {useAppSelector} from '../../../store'
import {
  useCreateBudgetTransactionMutation,
  useUpdateBudgetTransactionMutation,
  type BudgetAccountDto,
  type BudgetTransactionDto,
  type EnvelopeGroupDto,
} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

type Direction = 'Expense' | 'Income'

interface FormValues {
  accountId: string
  categoryId: string
  amount: number | null
  direction: Direction
  date: string
  notes: string
}

const DIRECTION_OPTIONS: {id: Direction; label: string}[] = [
  {id: 'Expense', label: 'Expense'},
  {id: 'Income',  label: 'Income'},
]

const UNCATEGORIZED_ID = '__uncategorized__'

function todayIso(): string {
  const d = new Date()
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

/**
 * Transaction dialog — creates or edits a `BudgetTransaction`. The user
 * enters a positive `amount` together with an Expense/Income toggle; we
 * flip the sign before submission so the backend always receives the
 * canonical signed amount (negative = expense). When `existing` is set,
 * the dialog opens in edit mode and the parent receives the updated DTO
 * via `onSaved`.
 */
export function TransactionDialog({
  accounts,
  groups,
  existing,
  onClose,
  onSaved,
  preset,
}: {
  accounts: BudgetAccountDto[]
  groups: EnvelopeGroupDto[]
  existing?: BudgetTransactionDto
  onClose: () => void
  onSaved?: (updated: BudgetTransactionDto) => void
  preset?: {accountId?: string; categoryId?: string}
}) {
  const {year, month} = useAppSelector(s => s.budget)
  const [createTx, {isLoading: isCreating}] = useCreateBudgetTransactionMutation()
  const [updateTx, {isLoading: isUpdating}] = useUpdateBudgetTransactionMutation()
  const isLoading = isCreating || isUpdating
  const [err, setErr] = useState<string | null>(null)

  const {control, handleSubmit, formState, watch} = useForm<FormValues>({
    defaultValues: {
      accountId: existing?.accountId ?? preset?.accountId ?? '',
      categoryId: existing?.categoryId ?? preset?.categoryId ?? UNCATEGORIZED_ID,
      amount: existing ? Math.abs(existing.amount) : null,
      direction: existing && existing.amount > 0 ? 'Income' : 'Expense',
      date: existing?.date?.slice(0, 10) ?? todayIso(),
      notes: existing?.notes ?? '',
    },
  })

  const direction = watch('direction')

  const accountOptions = accounts
    .filter(a => !a.isClosed)
    .map(a => ({id: a.id, label: a.name}))

  const categoryOptions = [
    {id: UNCATEGORIZED_ID, label: '— Uncategorized —'},
    ...groups.flatMap(g =>
      g.categories.map(c => ({
        id: c.categoryId,
        label: `${c.emoji ?? '•'} ${c.name}`,
      })),
    ),
  ]

  const onSubmit = handleSubmit(async values => {
    setErr(null)
    const magnitude = Number(values.amount ?? 0)
    const signed = values.direction === 'Expense' ? -magnitude : magnitude
    try {
      if (existing) {
        const updated = await updateTx({
          id: existing.id,
          year,
          month,
          accountId: values.accountId,
          categoryId: values.categoryId === UNCATEGORIZED_ID ? null : values.categoryId,
          amount: signed,
          date: values.date,
          notes: values.notes.trim() || null,
        }).unwrap()
        onSaved?.(updated)
        onClose()
        return
      }
      await createTx({
        accountId: values.accountId,
        categoryId: values.categoryId === UNCATEGORIZED_ID ? null : values.categoryId,
        amount: signed,
        date: values.date,
        notes: values.notes.trim() || null,
        year,
        month,
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
        <h3>{existing ? 'Edit Transaction' : 'New Transaction'}</h3>
        <div className="subtitle">
          {direction === 'Expense' ? 'Record money leaving an account.' : 'Record money received.'}
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Account</div>
          <Controller
            control={control}
            name="accountId"
            rules={{required: 'Choose an account.'}}
            render={({field}) => (
              <DropDownList
                dataSource={accountOptions}
                fields={{text: 'label', value: 'id'}}
                value={field.value || null}
                placeholder="Pick account"
                onChange={(e: {value: unknown}) => field.onChange((e.value as string) ?? '')}
              />
            )}
          />
          {formState.errors.accountId && (
            <p className="field-error">{formState.errors.accountId.message}</p>
          )}
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Category</div>
          <Controller
            control={control}
            name="categoryId"
            render={({field}) => (
              <DropDownList
                dataSource={categoryOptions}
                fields={{text: 'label', value: 'id'}}
                value={field.value || UNCATEGORIZED_ID}
                onChange={(e: {value: unknown}) =>
                  field.onChange((e.value as string) ?? UNCATEGORIZED_ID)
                }
              />
            )}
          />
        </div>

        <div className="budget-modal-row">
          <div className="budget-modal-field">
            <div className="budget-modal-label">Amount</div>
            <Controller
              control={control}
              name="amount"
              rules={{validate: v => (v != null && Number(v) > 0) || 'Must be positive.'}}
              render={({field}) => (
                <NumericTextBox
                  min={0}
                  value={field.value ?? null}
                  onChange={e => field.onChange((e.value as number | null) ?? null)}
                />
              )}
            />
            {formState.errors.amount && (
              <p className="field-error">{formState.errors.amount.message}</p>
            )}
          </div>

          <div className="budget-modal-field">
            <div className="budget-modal-label">Type</div>
            <Controller
              control={control}
              name="direction"
              render={({field}) => (
                <DropDownList
                  dataSource={DIRECTION_OPTIONS}
                  fields={{text: 'label', value: 'id'}}
                  value={field.value || 'Expense'}
                  onChange={(e: {value: unknown}) =>
                    field.onChange((e.value as Direction) ?? 'Expense')
                  }
                />
              )}
            />
          </div>
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Date</div>
          <Controller
            control={control}
            name="date"
            rules={{required: 'Pick a date.'}}
            render={({field}) => (
              <input
                type="date"
                className="budget-assigned-input"
                style={{width: '100%', textAlign: 'left'}}
                value={field.value}
                onChange={e => field.onChange(e.target.value)}
              />
            )}
          />
          {formState.errors.date && (
            <p className="field-error">{formState.errors.date.message}</p>
          )}
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Notes</div>
          <Controller
            control={control}
            name="notes"
            rules={{maxLength: {value: 500, message: 'Max 500 characters.'}}}
            render={({field}) => (
              <TextArea
                rows={2}
                value={field.value}
                onChange={e => field.onChange(e.value ?? '')}
              />
            )}
          />
          {formState.errors.notes && (
            <p className="field-error">{formState.errors.notes.message}</p>
          )}
        </div>

        {err && <p className="field-error">{err}</p>}

        <div className="budget-modal-footer">
          <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={isLoading}>
            {isLoading ? '…' : existing ? 'Save' : 'Add'}
          </Button>
        </div>
      </form>
    </div>
  )
}
