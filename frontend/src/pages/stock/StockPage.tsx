import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { Button, Color, Size, Variant } from '@syncfusion/react-buttons'
import { NumericTextBox } from '@syncfusion/react-inputs'
import { DropDownList } from '@syncfusion/react-dropdowns'
import {
  useDeleteStockMutation,
  useListIngredientsQuery,
  useListStockQuery,
  useUpsertStockMutation,
} from '../../shared/api/api'
import type { StockItemDto } from '../../shared/api/api'

interface AddStockForm {
  ingredientId: string
  quantity: number | null
}

export function StockPage() {
  const { data: stock, isLoading, error } = useListStockQuery()
  const { data: ingredients } = useListIngredientsQuery()
  const [upsertStock, { isLoading: isUpserting }] = useUpsertStockMutation()
  const [deleteStock] = useDeleteStockMutation()

  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const addForm = useForm<AddStockForm>({
    defaultValues: { ingredientId: '', quantity: null },
  })

  const stockedIds = new Set((stock ?? []).map((s) => s.ingredientId))
  const availableIngredients = (ingredients ?? []).filter((i) => !stockedIds.has(i.id))

  const onAdd = addForm.handleSubmit(async (values) => {
    setErrorMessage(null)
    const qty = Number(values.quantity)
    if (!(qty >= 0)) {
      addForm.setError('quantity', { type: 'validate', message: 'ต้องเป็นเลขไม่ติดลบ' })
      return
    }
    try {
      await upsertStock({ ingredientId: values.ingredientId, quantity: qty }).unwrap()
      addForm.reset({ ingredientId: '', quantity: null })
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  })

  const adjustQuantity = async (item: StockItemDto, delta: number) => {
    const next = Math.max(0, item.quantity + delta)
    if (next === item.quantity) return
    setErrorMessage(null)
    try {
      await upsertStock({ ingredientId: item.ingredientId, quantity: next }).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const setQuantity = async (item: StockItemDto, next: number | null | undefined) => {
    if (next == null || next < 0 || next === item.quantity) return
    setErrorMessage(null)
    try {
      await upsertStock({ ingredientId: item.ingredientId, quantity: next }).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleDelete = async (item: StockItemDto) => {
    if (!confirm(`ลบ "${item.ingredientName}" ออกจาก stock?`)) return
    setErrorMessage(null)
    try {
      await deleteStock(item.id).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  return (
    <section className="page page--stock">
      <header className="page__header">
        <h1>Stock</h1>
      </header>

      <form onSubmit={onAdd} className="row-add" noValidate style={{ alignItems: 'flex-start' }}>
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 4, minWidth: 180 }}>
          <Controller
            control={addForm.control}
            name="ingredientId"
            rules={{ required: 'กรุณาเลือกวัตถุดิบ' }}
            render={({ field }) => (
              <DropDownList
                dataSource={availableIngredients.map((i) => ({
                  id: i.id,
                  label: `${i.name} (${i.unit})`,
                }))}
                fields={{ text: 'label', value: 'id' }}
                value={field.value || null}
                placeholder={
                  availableIngredients.length === 0
                    ? 'วัตถุดิบทั้งหมดมีใน stock แล้ว'
                    : 'เลือกวัตถุดิบ *'
                }
                disabled={isUpserting || availableIngredients.length === 0}
                onChange={(e: { value: unknown }) => field.onChange((e.value as string) ?? '')}
              />
            )}
          />
          {addForm.formState.errors.ingredientId && (
            <p className="field-error">{addForm.formState.errors.ingredientId.message}</p>
          )}
        </div>

        <div style={{ width: 140, display: 'flex', flexDirection: 'column', gap: 4 }}>
          <Controller
            control={addForm.control}
            name="quantity"
            rules={{
              validate: (v) => (v != null && v >= 0) || 'กรอกจำนวน (≥ 0)',
            }}
            render={({ field }) => (
              <NumericTextBox
                placeholder="จำนวน *"
                min={0}
                disabled={isUpserting}
                value={field.value ?? null}
                onChange={(e) => field.onChange((e.value as number | null) ?? null)}
              />
            )}
          />
          {addForm.formState.errors.quantity && (
            <p className="field-error">{addForm.formState.errors.quantity.message}</p>
          )}
        </div>

        <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={isUpserting}>
          {isUpserting ? '...' : '+ เพิ่ม'}
        </Button>
      </form>

      {errorMessage && <div className="error-banner">{errorMessage}</div>}

      {isLoading && <p>Loading…</p>}
      {error && !isLoading && <p>Failed to load stock.</p>}
      {stock && stock.length === 0 && !isLoading && (
        <p style={{ textAlign: 'center', padding: 32, color: 'var(--color-text-muted)' }}>
          ยังไม่มี stock — เพิ่มวัตถุดิบด้านบนได้เลย
        </p>
      )}

      {stock && stock.length > 0 && (
        <div className="table-scroll">
          <table className="data-table">
            <thead>
              <tr>
                <th>วัตถุดิบ</th>
                <th style={{ width: 260 }}>คงเหลือ</th>
                <th>อัปเดตล่าสุด</th>
                <th style={{ width: 80 }}></th>
              </tr>
            </thead>
            <tbody>
              {stock.map((item) => (
                <tr key={item.id} className={item.quantity === 0 ? 'row--empty' : undefined}>
                  <td>{item.ingredientName}</td>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                      <Button
                        size={Size.Small}
                        variant={Variant.Outlined}
                        color={Color.Secondary}
                        onClick={() => adjustQuantity(item, -1)}
                        disabled={item.quantity <= 0}
                        aria-label="decrease"
                      >
                        −
                      </Button>
                      <div style={{ width: 90 }}>
                        <NumericTextBox
                          min={0}
                          value={item.quantity}
                          onChange={(e) => setQuantity(item, e.value as number | null | undefined)}
                        />
                      </div>
                      <Button
                        size={Size.Small}
                        variant={Variant.Outlined}
                        color={Color.Secondary}
                        onClick={() => adjustQuantity(item, 1)}
                        aria-label="increase"
                      >
                        +
                      </Button>
                      <span style={{ color: 'var(--color-text-muted)', fontSize: 13 }}>
                        {item.unit}
                      </span>
                    </div>
                  </td>
                  <td style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>
                    {new Date(item.updatedAt).toLocaleString('th-TH')}
                  </td>
                  <td>
                    <Button
                      size={Size.Small}
                      variant={Variant.Outlined}
                      color={Color.Error}
                      onClick={() => handleDelete(item)}
                      aria-label="delete"
                    >
                      🗑️
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}

function getErrorMessage(err: unknown): string {
  if (typeof err === 'object' && err !== null && 'data' in err) {
    const data = (err as { data?: { detail?: string; title?: string; errors?: Record<string, string[]> } }).data
    if (data?.errors) {
      const first = Object.values(data.errors)[0]?.[0]
      if (first) return first
    }
    if (data?.detail) return data.detail
    if (data?.title) return data.title
  }
  return 'Something went wrong. Please try again.'
}
