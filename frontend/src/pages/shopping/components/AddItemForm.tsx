import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { NumericTextBox } from '@syncfusion/react-inputs'
import { Autocomplete } from '@syncfusion/react-dropdowns'
import { useListIngredientsQuery } from '../../../shared/api/api'
import type { ShoppingListItemDto } from '../../../shared/api/api'

interface AddItemFormProps {
  existingItems: ShoppingListItemDto[]
  onAdd: (ingredientId: string, quantity: number) => Promise<void>
  isAdding: boolean
}

interface AddItemFormValues {
  quantity: number | null
}

export function AddItemForm({ existingItems, onAdd, isAdding }: AddItemFormProps) {
  const { data: allIngredients, isLoading: isLoadingIngredients } = useListIngredientsQuery()
  const [pickerValue, setPickerValue] = useState('')
  const [pickerError, setPickerError] = useState<string | null>(null)

  const { control, handleSubmit, reset, formState: { errors } } = useForm<AddItemFormValues>({
    defaultValues: { quantity: 1 },
  })

  const existingIngredientIds = new Set(existingItems.map((i) => i.ingredientId))

  const availableIngredients = (allIngredients ?? []).filter(
    (i) => !existingIngredientIds.has(i.ingredientId),
  )

  const onSubmit = handleSubmit(async ({ quantity }) => {
    setPickerError(null)
    const typed = pickerValue.trim()
    if (!typed) {
      setPickerError('กรุณาเลือกวัตถุดิบ')
      return
    }
    const match = (allIngredients ?? []).find(
      (i) => i.name.trim().toLowerCase() === typed.toLowerCase(),
    )
    if (!match) {
      setPickerError(`ไม่พบ "${typed}" ในรายการวัตถุดิบ`)
      return
    }
    if (existingIngredientIds.has(match.ingredientId)) {
      setPickerError(`"${match.name}" อยู่ในรายการนี้แล้ว`)
      return
    }
    if (!quantity || quantity <= 0) {
      setPickerError('จำนวนต้องเป็นเลขบวก')
      return
    }
    await onAdd(match.ingredientId, quantity)
    setPickerValue('')
    reset({ quantity: 1 })
  })

  return (
    <div style={{ borderTop: '1px solid var(--color-border)', paddingTop: 16, marginTop: 8 }}>
      <h3 style={{ margin: '0 0 10px', fontSize: 15 }}>+ เพิ่มรายการ</h3>
      <form onSubmit={onSubmit} noValidate>
        <div className="row-add" style={{ alignItems: 'flex-start' }}>
          <div style={{ flex: 1, minWidth: 180, display: 'flex', flexDirection: 'column', gap: 4 }}>
            <Autocomplete
              dataSource={availableIngredients.map((i) => ({
                id: i.ingredientId,
                label: i.name,
              }))}
              fields={{ text: 'label', value: 'label' }}
              value={pickerValue}
              placeholder="ชื่อวัตถุดิบ"
              disabled={isLoadingIngredients || isAdding}
              onChange={(e: { value: unknown }) => {
                setPickerValue((e.value as string) ?? '')
                setPickerError(null)
              }}
            />
          </div>
          <div style={{ width: 120, display: 'flex', flexDirection: 'column', gap: 4 }}>
            <Controller
              control={control}
              name="quantity"
              rules={{ validate: (v) => (v != null && v > 0) || 'ต้องเป็นเลขบวก' }}
              render={({ field }) => (
                <NumericTextBox
                  min={0.01}
                  value={field.value ?? null}
                  placeholder="จำนวน"
                  disabled={isAdding}
                  onChange={(e) => field.onChange((e.value as number | null) ?? null)}
                />
              )}
            />
            {errors.quantity && <p className="field-error">{errors.quantity.message}</p>}
          </div>
          <Button
            type="submit"
            variant={Variant.Filled}
            color={Color.Primary}
            disabled={isAdding}
          >
            {isAdding ? '...' : '+ เพิ่ม'}
          </Button>
        </div>
        {pickerError && <p className="field-error">{pickerError}</p>}
      </form>
    </div>
  )
}
