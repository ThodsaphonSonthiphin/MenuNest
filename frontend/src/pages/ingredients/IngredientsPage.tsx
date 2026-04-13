import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import {
  useListIngredientsQuery,
  useCreateIngredientMutation,
  useUpdateIngredientMutation,
  useDeleteIngredientMutation,
} from '../../shared/api/api'
import type { IngredientDto } from '../../shared/api/api'

interface IngredientForm {
  name: string
  unit: string
}

export function IngredientsPage() {
  const { data, isLoading, error } = useListIngredientsQuery()
  const [createIngredient, { isLoading: isCreating }] = useCreateIngredientMutation()
  const [updateIngredient] = useUpdateIngredientMutation()
  const [deleteIngredient] = useDeleteIngredientMutation()

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
    if (!confirm(`ลบ "${ingredient.name}" หรือไม่?`)) return
    setErrorMessage(null)
    try {
      await deleteIngredient(ingredient.id).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  // Pre-populate the edit form when a different row is picked.
  useEffect(() => {
    if (!editingId || !data) return
    const source = data.find((i) => i.id === editingId)
    if (source) {
      editForm.reset({ name: source.name, unit: source.unit })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editingId])

  return (
    <section className="page page--ingredients">
      <header className="page__header">
        <h1>Ingredients</h1>
      </header>

      <p style={{ color: 'var(--color-text-muted)', marginBottom: 16 }}>
        Master list นี้ใช้เป็น autocomplete เวลาสร้าง recipe / เพิ่ม stock — 1 ingredient = 1 หน่วย
      </p>

      <form onSubmit={onAdd} className="ingredient-add" noValidate>
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 4 }}>
          <input
            type="text"
            placeholder="ชื่อวัตถุดิบ * (เช่น ไข่ไก่)"
            aria-invalid={addForm.formState.errors.name ? 'true' : 'false'}
            disabled={isCreating}
            {...addForm.register('name', {
              required: 'กรุณากรอกชื่อวัตถุดิบ',
              maxLength: { value: 120, message: 'ยาวเกิน 120 ตัวอักษร' },
              validate: (v) => v.trim().length > 0 || 'กรุณากรอกชื่อวัตถุดิบ',
            })}
          />
          {addForm.formState.errors.name && (
            <p className="field-error">{addForm.formState.errors.name.message}</p>
          )}
        </div>

        <div style={{ width: 140, display: 'flex', flexDirection: 'column', gap: 4 }}>
          <input
            type="text"
            placeholder="หน่วย * (เช่น ฟอง)"
            aria-invalid={addForm.formState.errors.unit ? 'true' : 'false'}
            disabled={isCreating}
            {...addForm.register('unit', {
              required: 'กรุณากรอกหน่วย',
              maxLength: { value: 40, message: 'ยาวเกิน 40 ตัวอักษร' },
              validate: (v) => v.trim().length > 0 || 'กรุณากรอกหน่วย',
            })}
          />
          {addForm.formState.errors.unit && (
            <p className="field-error">{addForm.formState.errors.unit.message}</p>
          )}
        </div>

        <button type="submit" className="btn btn--primary" disabled={isCreating}>
          {isCreating ? '...' : '+ เพิ่ม'}
        </button>
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
        <table className="data-table">
          <thead>
            <tr>
              <th>ชื่อ</th>
              <th>หน่วย</th>
              <th style={{ width: 180 }}>การทำงาน</th>
            </tr>
          </thead>
          <tbody>
            {data.map((ingredient) =>
              editingId === ingredient.id ? (
                <tr key={ingredient.id}>
                  <td>
                    <input
                      aria-invalid={editForm.formState.errors.name ? 'true' : 'false'}
                      {...editForm.register('name', {
                        required: 'กรุณากรอกชื่อ',
                        maxLength: { value: 120, message: 'ยาวเกิน 120 ตัวอักษร' },
                        validate: (v) => v.trim().length > 0 || 'กรุณากรอกชื่อ',
                      })}
                    />
                    {editForm.formState.errors.name && (
                      <p className="field-error">{editForm.formState.errors.name.message}</p>
                    )}
                  </td>
                  <td>
                    <input
                      aria-invalid={editForm.formState.errors.unit ? 'true' : 'false'}
                      {...editForm.register('unit', {
                        required: 'กรุณากรอกหน่วย',
                        maxLength: { value: 40, message: 'ยาวเกิน 40 ตัวอักษร' },
                        validate: (v) => v.trim().length > 0 || 'กรุณากรอกหน่วย',
                      })}
                    />
                    {editForm.formState.errors.unit && (
                      <p className="field-error">{editForm.formState.errors.unit.message}</p>
                    )}
                  </td>
                  <td>
                    <button type="button" className="btn btn--primary btn--sm" onClick={onSaveEdit}>
                      Save
                    </button>{' '}
                    <button type="button" className="btn btn--outline btn--sm" onClick={cancelEdit}>
                      Cancel
                    </button>
                  </td>
                </tr>
              ) : (
                <tr key={ingredient.id}>
                  <td>{ingredient.name}</td>
                  <td>{ingredient.unit}</td>
                  <td>
                    <button
                      type="button"
                      className="btn btn--outline btn--sm"
                      onClick={() => startEdit(ingredient)}
                    >
                      ✏️ Edit
                    </button>{' '}
                    <button
                      type="button"
                      className="btn btn--outline btn--sm"
                      onClick={() => handleDelete(ingredient)}
                    >
                      🗑️ Delete
                    </button>
                  </td>
                </tr>
              ),
            )}
          </tbody>
        </table>
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
