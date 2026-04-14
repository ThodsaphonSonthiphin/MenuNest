import { useEffect, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { Button, Color, Size, Variant } from '@syncfusion/react-buttons'
import { TextBox } from '@syncfusion/react-inputs'
import { Grid, Column, Columns } from '@syncfusion/react-grid'
import type { ColumnTemplateProps } from '@syncfusion/react-grid'
import {
  useListIngredientsQuery,
  useCreateIngredientMutation,
  useUpdateIngredientMutation,
  useDeleteIngredientMutation,
} from '../../shared/api/api'
import type { IngredientDto } from '../../shared/api/api'
import { useConfirm } from '../../shared/hooks/useConfirm'

interface IngredientForm {
  name: string
  unit: string
}

export function IngredientsPage() {
  const { data, isLoading, error } = useListIngredientsQuery()
  const [createIngredient, { isLoading: isCreating }] = useCreateIngredientMutation()
  const [updateIngredient] = useUpdateIngredientMutation()
  const [deleteIngredient] = useDeleteIngredientMutation()
  const { confirm } = useConfirm()

  const [editingId, setEditingId] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const addForm = useForm<IngredientForm>({ defaultValues: { name: '', unit: '' } })
  const editForm = useForm<IngredientForm>({ defaultValues: { name: '', unit: '' } })

  const onAdd = addForm.handleSubmit(async (values) => {
    setErrorMessage(null)
    try {
      await createIngredient({ name: values.name.trim(), unit: values.unit.trim() }).unwrap()
      addForm.reset()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  })

  const startEdit = (ingredient: IngredientDto) => {
    setEditingId(ingredient.id)
    setErrorMessage(null)
    editForm.reset({ name: ingredient.name, unit: ingredient.unit })
  }

  const cancelEdit = () => {
    setEditingId(null)
    editForm.reset()
  }

  const onSaveEdit = editForm.handleSubmit(async (values) => {
    if (!editingId) return
    setErrorMessage(null)
    try {
      await updateIngredient({
        id: editingId,
        name: values.name.trim(),
        unit: values.unit.trim(),
      }).unwrap()
      cancelEdit()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  })

  const handleDelete = async (ingredient: IngredientDto) => {
    const ok = await confirm({
      title: 'ลบวัตถุดิบ',
      message: (
        <>
          ลบ <strong>"{ingredient.name}"</strong> หรือไม่?
        </>
      ),
      confirmText: 'ลบ',
      destructive: true,
    })
    if (!ok) return
    setErrorMessage(null)
    try {
      await deleteIngredient(ingredient.id).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  useEffect(() => {
    if (!editingId || !data) return
    const source = data.find((i) => i.id === editingId)
    if (source) editForm.reset({ name: source.name, unit: source.unit })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editingId])

  const NameTemplate = ({ data: row }: ColumnTemplateProps<IngredientDto>) => {
    if (editingId === row.id) {
      return (
        <div>
          <Controller
            control={editForm.control}
            name="name"
            rules={{
              required: 'กรุณากรอกชื่อ',
              maxLength: { value: 120, message: 'ยาวเกิน 120 ตัวอักษร' },
              validate: (v) => v.trim().length > 0 || 'กรุณากรอกชื่อ',
            }}
            render={({ field }) => (
              <TextBox value={field.value} onChange={(e) => field.onChange(e.value ?? '')} />
            )}
          />
          {editForm.formState.errors.name && (
            <p className="field-error">{editForm.formState.errors.name.message}</p>
          )}
        </div>
      )
    }
    return <span>{row.name}</span>
  }

  const UnitTemplate = ({ data: row }: ColumnTemplateProps<IngredientDto>) => {
    if (editingId === row.id) {
      return (
        <div>
          <Controller
            control={editForm.control}
            name="unit"
            rules={{
              required: 'กรุณากรอกหน่วย',
              maxLength: { value: 40, message: 'ยาวเกิน 40 ตัวอักษร' },
              validate: (v) => v.trim().length > 0 || 'กรุณากรอกหน่วย',
            }}
            render={({ field }) => (
              <TextBox value={field.value} onChange={(e) => field.onChange(e.value ?? '')} />
            )}
          />
          {editForm.formState.errors.unit && (
            <p className="field-error">{editForm.formState.errors.unit.message}</p>
          )}
        </div>
      )
    }
    return <span>{row.unit}</span>
  }

  const ActionsTemplate = ({ data: row }: ColumnTemplateProps<IngredientDto>) => {
    if (editingId === row.id) {
      return (
        <div>
          <Button
            size={Size.Small}
            variant={Variant.Filled}
            color={Color.Primary}
            onClick={onSaveEdit}
          >
            Save
          </Button>{' '}
          <Button
            size={Size.Small}
            variant={Variant.Outlined}
            color={Color.Secondary}
            onClick={cancelEdit}
          >
            Cancel
          </Button>
        </div>
      )
    }
    return (
      <div>
        <Button
          size={Size.Small}
          variant={Variant.Outlined}
          color={Color.Primary}
          onClick={() => startEdit(row)}
        >
          ✏️ Edit
        </Button>{' '}
        <Button
          size={Size.Small}
          variant={Variant.Outlined}
          color={Color.Error}
          onClick={() => handleDelete(row)}
        >
          🗑️ Delete
        </Button>
      </div>
    )
  }

  return (
    <section className="page page--ingredients">
      <header className="page__header">
        <h1>Ingredients</h1>
      </header>

      <p style={{ color: 'var(--color-text-muted)', marginBottom: 16 }}>
        Master list นี้ใช้เป็น autocomplete เวลาสร้าง recipe / เพิ่ม stock — 1 ingredient = 1 หน่วย
      </p>

      <form onSubmit={onAdd} className="ingredient-add" noValidate>
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 4, minWidth: 180 }}>
          <Controller
            control={addForm.control}
            name="name"
            rules={{
              required: 'กรุณากรอกชื่อวัตถุดิบ',
              maxLength: { value: 120, message: 'ยาวเกิน 120 ตัวอักษร' },
              validate: (v) => v.trim().length > 0 || 'กรุณากรอกชื่อวัตถุดิบ',
            }}
            render={({ field }) => (
              <TextBox
                placeholder="ชื่อวัตถุดิบ * (เช่น ไข่ไก่)"
                disabled={isCreating}
                value={field.value}
                onChange={(e) => field.onChange(e.value ?? '')}
              />
            )}
          />
          {addForm.formState.errors.name && (
            <p className="field-error">{addForm.formState.errors.name.message}</p>
          )}
        </div>

        <div style={{ width: 140, display: 'flex', flexDirection: 'column', gap: 4 }}>
          <Controller
            control={addForm.control}
            name="unit"
            rules={{
              required: 'กรุณากรอกหน่วย',
              maxLength: { value: 40, message: 'ยาวเกิน 40 ตัวอักษร' },
              validate: (v) => v.trim().length > 0 || 'กรุณากรอกหน่วย',
            }}
            render={({ field }) => (
              <TextBox
                placeholder="หน่วย * (เช่น ฟอง)"
                disabled={isCreating}
                value={field.value}
                onChange={(e) => field.onChange(e.value ?? '')}
              />
            )}
          />
          {addForm.formState.errors.unit && (
            <p className="field-error">{addForm.formState.errors.unit.message}</p>
          )}
        </div>

        <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={isCreating}>
          {isCreating ? '...' : '+ เพิ่ม'}
        </Button>
      </form>

      {errorMessage && <div className="error-banner">{errorMessage}</div>}

      {isLoading && <p>Loading…</p>}
      {error && !isLoading && <p>Failed to load ingredients.</p>}
      {data && data.length === 0 && !isLoading && (
        <p style={{ textAlign: 'center', padding: 32, color: 'var(--color-text-muted)' }}>
          ยังไม่มี ingredient — เริ่มเพิ่มด้านบนได้เลย
        </p>
      )}

      {data && data.length > 0 && (
        <Grid dataSource={data as IngredientDto[]} height="auto">
          <Columns>
            <Column field="name" headerText="ชื่อ" template={NameTemplate} />
            <Column field="unit" headerText="หน่วย" template={UnitTemplate} />
            <Column headerText="การทำงาน" width={220} template={ActionsTemplate} />
          </Columns>
        </Grid>
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
