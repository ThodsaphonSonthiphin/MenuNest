import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {DropDownList} from '@syncfusion/react-dropdowns'
import {NumericTextBox, TextBox} from '@syncfusion/react-inputs'
import {
  useCreateBudgetCategoryMutation,
  useListBudgetGroupsQuery,
  type BudgetTargetType,
} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'

interface FormValues {
  groupId: string
  name: string
  emoji: string
  targetType: BudgetTargetType
  targetAmount: number | null
  targetDueDate: string
  targetDayOfMonth: number | null
}

const TARGET_TYPE_OPTIONS: {id: BudgetTargetType; label: string}[] = [
  {id: 'None',          label: 'No target'},
  {id: 'MonthlyAmount', label: 'Monthly amount'},
  {id: 'ByDate',        label: 'By date'},
]

/**
 * Add Category dialog — creates a `BudgetCategory` within an existing group.
 * Target fields are conditionally rendered:
 *   - MonthlyAmount → amount + optional day-of-month (1..31)
 *   - ByDate        → amount + due date (required)
 *   - None          → no target fields
 *
 * We submit empty strings as null to keep the backend DTO clean.
 */
export function AddCategoryDialog({onClose}: {onClose: () => void}) {
  const {data: groups, isLoading: groupsLoading} = useListBudgetGroupsQuery()
  const [createCategory, {isLoading}] = useCreateBudgetCategoryMutation()
  const [err, setErr] = useState<string | null>(null)
  const {control, handleSubmit, formState, watch} = useForm<FormValues>({
    defaultValues: {
      groupId: '',
      name: '',
      emoji: '',
      targetType: 'None',
      targetAmount: null,
      targetDueDate: '',
      targetDayOfMonth: null,
    },
  })

  const targetType = watch('targetType')

  const groupOptions = (groups ?? []).map(g => ({id: g.id, label: g.name}))

  const onSubmit = handleSubmit(async values => {
    setErr(null)
    try {
      await createCategory({
        groupId: values.groupId,
        name: values.name.trim(),
        emoji: values.emoji.trim() || null,
        sortOrder: 0,
        targetType: values.targetType,
        targetAmount: values.targetType === 'None'
          ? null
          : values.targetAmount == null ? null : Number(values.targetAmount),
        targetDueDate: values.targetType === 'ByDate' && values.targetDueDate
          ? values.targetDueDate
          : null,
        targetDayOfMonth: values.targetType === 'MonthlyAmount' && values.targetDayOfMonth != null
          ? Number(values.targetDayOfMonth)
          : null,
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
        <h3>Add Category</h3>
        <div className="subtitle">Pick a group and (optionally) set a funding target.</div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Group</div>
          <Controller
            control={control}
            name="groupId"
            rules={{required: 'Pick a group.'}}
            render={({field}) => (
              <DropDownList
                dataSource={groupOptions}
                fields={{text: 'label', value: 'id'}}
                value={field.value || null}
                placeholder={groupsLoading ? 'Loading…' : 'Select group'}
                disabled={groupsLoading || groupOptions.length === 0}
                onChange={(e: {value: unknown}) => field.onChange((e.value as string) ?? '')}
              />
            )}
          />
          {formState.errors.groupId && (
            <p className="field-error">{formState.errors.groupId.message}</p>
          )}
        </div>

        <div className="budget-modal-row">
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
                  placeholder="e.g. Internet"
                  onChange={e => field.onChange(e.value ?? '')}
                />
              )}
            />
            {formState.errors.name && (
              <p className="field-error">{formState.errors.name.message}</p>
            )}
          </div>

          <div className="budget-modal-field" style={{flex: '0 0 80px'}}>
            <div className="budget-modal-label">Emoji</div>
            <Controller
              control={control}
              name="emoji"
              rules={{maxLength: {value: 8, message: 'Max 8 chars.'}}}
              render={({field}) => (
                <TextBox
                  value={field.value}
                  placeholder="🌐"
                  onChange={e => field.onChange(e.value ?? '')}
                />
              )}
            />
            {formState.errors.emoji && (
              <p className="field-error">{formState.errors.emoji.message}</p>
            )}
          </div>
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Target</div>
          <Controller
            control={control}
            name="targetType"
            render={({field}) => (
              <DropDownList
                dataSource={TARGET_TYPE_OPTIONS}
                fields={{text: 'label', value: 'id'}}
                value={field.value || null}
                onChange={(e: {value: unknown}) =>
                  field.onChange((e.value as BudgetTargetType) ?? 'None')
                }
              />
            )}
          />
        </div>

        {targetType !== 'None' && (
          <div className="budget-modal-field">
            <div className="budget-modal-label">Target amount</div>
            <Controller
              control={control}
              name="targetAmount"
              rules={{validate: v => (v != null && Number(v) > 0) || 'Must be positive.'}}
              render={({field}) => (
                <NumericTextBox
                  min={0}
                  value={field.value ?? null}
                  onChange={e => field.onChange((e.value as number | null) ?? null)}
                />
              )}
            />
            {formState.errors.targetAmount && (
              <p className="field-error">{formState.errors.targetAmount.message}</p>
            )}
          </div>
        )}

        {targetType === 'ByDate' && (
          <div className="budget-modal-field">
            <div className="budget-modal-label">Due date</div>
            <Controller
              control={control}
              name="targetDueDate"
              rules={{required: 'Pick a due date.'}}
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
            {formState.errors.targetDueDate && (
              <p className="field-error">{formState.errors.targetDueDate.message}</p>
            )}
          </div>
        )}

        {targetType === 'MonthlyAmount' && (
          <div className="budget-modal-field">
            <div className="budget-modal-label">Day of month (optional)</div>
            <Controller
              control={control}
              name="targetDayOfMonth"
              rules={{
                validate: v =>
                  v == null || (Number(v) >= 1 && Number(v) <= 31) || 'Must be 1..31.',
              }}
              render={({field}) => (
                <NumericTextBox
                  min={1}
                  max={31}
                  value={field.value ?? null}
                  onChange={e => field.onChange((e.value as number | null) ?? null)}
                />
              )}
            />
            {formState.errors.targetDayOfMonth && (
              <p className="field-error">{formState.errors.targetDayOfMonth.message}</p>
            )}
          </div>
        )}

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
