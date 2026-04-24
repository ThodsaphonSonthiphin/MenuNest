import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {DropDownList} from '@syncfusion/react-dropdowns'
import {NumericTextBox} from '@syncfusion/react-inputs'
import {useAppSelector} from '../../../store'
import {useMoveMoneyMutation, type EnvelopeDto, type EnvelopeGroupDto} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {formatTHB} from '../BudgetPage.hooks'

interface FormValues {
  toCategoryId: string
  amount: number | null
}

/**
 * Move Money dialog — transfer available money from one envelope to another
 * for the current budget month. Destination dropdown lists every other
 * category with its current available balance as context.
 */
export function MoveMoneyDialog({from, groups, onClose}: {
  from: EnvelopeDto
  groups: EnvelopeGroupDto[]
  onClose: () => void
}) {
  const {year, month} = useAppSelector(s => s.budget)
  const [move, {isLoading}] = useMoveMoneyMutation()
  const [err, setErr] = useState<string | null>(null)
  const {control, handleSubmit, formState} = useForm<FormValues>({
    defaultValues: {toCategoryId: '', amount: null},
  })

  const options = groups
    .flatMap(g => g.categories)
    .filter(c => c.categoryId !== from.categoryId)
    .map(c => ({
      id: c.categoryId,
      label: `${c.emoji ?? '•'} ${c.name} (${formatTHB(c.available)})`,
    }))

  const onSubmit = handleSubmit(async values => {
    setErr(null)
    try {
      await move({
        fromCategoryId: from.categoryId,
        toCategoryId: values.toCategoryId,
        year,
        month,
        amount: Number(values.amount),
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
        <h3>Move Money</h3>
        <div className="subtitle">
          From <strong>{from.name}</strong> · available {formatTHB(from.available)}
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">To category</div>
          <Controller
            control={control}
            name="toCategoryId"
            rules={{required: 'Choose a destination.'}}
            render={({field}) => (
              <DropDownList
                dataSource={options}
                fields={{text: 'label', value: 'id'}}
                value={field.value || null}
                placeholder="Pick destination…"
                onChange={(e: {value: unknown}) => field.onChange((e.value as string) ?? '')}
              />
            )}
          />
          {formState.errors.toCategoryId && (
            <p className="field-error">{formState.errors.toCategoryId.message}</p>
          )}
        </div>

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

        {err && <p className="field-error">{err}</p>}

        <div className="budget-modal-footer">
          <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={isLoading}>
            {isLoading ? '…' : 'Move'}
          </Button>
        </div>
      </form>
    </div>
  )
}
