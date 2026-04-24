import {useState} from 'react'
import {Controller, useForm} from 'react-hook-form'
import {Button, Color, Variant} from '@syncfusion/react-buttons'
import {DropDownList} from '@syncfusion/react-dropdowns'
import {NumericTextBox} from '@syncfusion/react-inputs'
import {useAppSelector} from '../../../store'
import {
  useCoverOverspendingMutation,
  type EnvelopeDto,
  type EnvelopeGroupDto,
} from '../../../shared/api/api'
import {getErrorMessage} from '../../../shared/utils/getErrorMessage'
import {formatTHB} from '../BudgetPage.hooks'

interface FormValues {
  fromCategoryId: string
  amount: number | null
}

/**
 * Cover Overspending dialog — pulls positive "available" from another
 * envelope to zero out a negative envelope. Shares the same shape as
 * `MoveMoneyDialog`, but the source dropdown is filtered to categories
 * with `available > 0`, and the amount pre-fills with the overspent magnitude.
 */
export function CoverOverspendingDialog({overspent, groups, onClose}: {
  overspent: EnvelopeDto
  groups: EnvelopeGroupDto[]
  onClose: () => void
}) {
  const {year, month} = useAppSelector(s => s.budget)
  const [cover, {isLoading}] = useCoverOverspendingMutation()
  const [err, setErr] = useState<string | null>(null)

  const defaultAmount = Math.abs(overspent.available)
  const {control, handleSubmit, formState} = useForm<FormValues>({
    defaultValues: {fromCategoryId: '', amount: defaultAmount},
  })

  const options = groups
    .flatMap(g => g.categories)
    .filter(c => c.categoryId !== overspent.categoryId && c.available > 0)
    .map(c => ({
      id: c.categoryId,
      label: `${c.emoji ?? '•'} ${c.name} (${formatTHB(c.available)})`,
    }))

  const onSubmit = handleSubmit(async values => {
    setErr(null)
    try {
      await cover({
        overspentCategoryId: overspent.categoryId,
        fromCategoryId: values.fromCategoryId,
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
        <h3>Cover Overspending</h3>
        <div className="subtitle">
          <strong>{overspent.name}</strong> is overspent by {formatTHB(defaultAmount)}
        </div>

        <div className="budget-modal-field">
          <div className="budget-modal-label">Cover from</div>
          <Controller
            control={control}
            name="fromCategoryId"
            rules={{required: 'Choose a source category.'}}
            render={({field}) => (
              <DropDownList
                dataSource={options}
                fields={{text: 'label', value: 'id'}}
                value={field.value || null}
                placeholder={
                  options.length === 0
                    ? 'No categories with available money'
                    : 'Pick source…'
                }
                disabled={options.length === 0}
                onChange={(e: {value: unknown}) => field.onChange((e.value as string) ?? '')}
              />
            )}
          />
          {formState.errors.fromCategoryId && (
            <p className="field-error">{formState.errors.fromCategoryId.message}</p>
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
          <Button
            type="submit"
            variant={Variant.Filled}
            color={Color.Primary}
            disabled={isLoading || options.length === 0}
          >
            {isLoading ? '…' : 'Cover'}
          </Button>
        </div>
      </form>
    </div>
  )
}
