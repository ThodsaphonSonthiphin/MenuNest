import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useFieldArray, useForm } from 'react-hook-form'
import {
  useCreateIngredientMutation,
  useCreateRecipeMutation,
  useDeleteRecipeMutation,
  useGetRecipeQuery,
  useListIngredientsQuery,
  useUpdateRecipeMutation,
} from '../../shared/api/api'

interface RecipeFormLine {
  ingredientId: string
  ingredientName: string
  unit: string
  quantity: string // keep as string so the user can clear it
}

interface RecipeFormValues {
  name: string
  description: string
  lines: RecipeFormLine[]
}

export function RecipeDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const isNew = !id || id === 'new'

  const { data: recipe, isLoading: isLoadingRecipe, error: recipeError } = useGetRecipeQuery(id!, {
    skip: isNew,
  })
  const { data: masterIngredients, isLoading: isLoadingIngredients } = useListIngredientsQuery()
  const [createRecipe, { isLoading: isCreating }] = useCreateRecipeMutation()
  const [updateRecipe, { isLoading: isUpdating }] = useUpdateRecipeMutation()
  const [deleteRecipe, { isLoading: isDeleting }] = useDeleteRecipeMutation()
  const [createIngredient] = useCreateIngredientMutation()

  const {
    register,
    handleSubmit,
    reset,
    control,
    formState: { errors },
  } = useForm<RecipeFormValues>({
    defaultValues: { name: '', description: '', lines: [] },
  })
  const { fields, append, remove } = useFieldArray({ control, name: 'lines' })

  const [pickerName, setPickerName] = useState('')
  const [pickerUnit, setPickerUnit] = useState('')
  const [pickerError, setPickerError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  // Populate from fetched recipe.
  useEffect(() => {
    if (!recipe) return
    reset({
      name: recipe.name,
      description: recipe.description ?? '',
      lines: recipe.ingredients.map((ri) => ({
        ingredientId: ri.ingredientId,
        ingredientName: ri.ingredientName,
        unit: ri.unit,
        quantity: ri.quantity.toString(),
      })),
    })
  }, [recipe, reset])

  const usedIngredientIds = useMemo(
    () => new Set(fields.map((f) => f.ingredientId)),
    [fields],
  )
  const availableIngredients = (masterIngredients ?? []).filter(
    (i) => !usedIngredientIds.has(i.id),
  )

  const handleAddLine = async () => {
    setPickerError(null)
    const typed = pickerName.trim()
    if (!typed) {
      setPickerError('กรุณากรอกชื่อวัตถุดิบ')
      return
    }

    const match = (masterIngredients ?? []).find(
      (i) => i.name.trim().toLowerCase() === typed.toLowerCase(),
    )

    if (match) {
      if (usedIngredientIds.has(match.id)) {
        setPickerError(`"${match.name}" อยู่ใน recipe นี้แล้ว`)
        return
      }
      append({
        ingredientId: match.id,
        ingredientName: match.name,
        unit: match.unit,
        quantity: '1',
      })
      setPickerName('')
      setPickerUnit('')
      return
    }

    const unit = pickerUnit.trim()
    if (!unit) {
      setPickerError(`"${typed}" เป็นวัตถุดิบใหม่ — กรุณากรอกหน่วยด้วย`)
      return
    }

    try {
      const created = await createIngredient({ name: typed, unit }).unwrap()
      append({
        ingredientId: created.id,
        ingredientName: created.name,
        unit: created.unit,
        quantity: '1',
      })
      setPickerName('')
      setPickerUnit('')
    } catch (err) {
      setPickerError(getErrorMessage(err))
    }
  }

  const onSave = handleSubmit(async (values) => {
    setFormError(null)
    if (values.lines.length === 0) {
      setFormError('กรุณาเพิ่มวัตถุดิบอย่างน้อย 1 รายการ')
      return
    }

    const payloadLines = values.lines.map((l) => ({
      ingredientId: l.ingredientId,
      quantity: Number(l.quantity),
    }))

    try {
      if (isNew) {
        const created = await createRecipe({
          name: values.name.trim(),
          description: values.description.trim() || null,
          ingredients: payloadLines,
        }).unwrap()
        navigate(`/recipes/${created.id}`, { replace: true })
      } else {
        await updateRecipe({
          id: id!,
          name: values.name.trim(),
          description: values.description.trim() || null,
          ingredients: payloadLines,
        }).unwrap()
      }
    } catch (err) {
      setFormError(getErrorMessage(err))
    }
  })

  const handleDelete = async () => {
    if (!id || isNew) return
    if (!confirm('ลบ recipe นี้หรือไม่?')) return
    setFormError(null)
    try {
      await deleteRecipe(id).unwrap()
      navigate('/recipes', { replace: true })
    } catch (err) {
      setFormError(getErrorMessage(err))
    }
  }

  if (!isNew && isLoadingRecipe) return <p style={{ padding: 32 }}>Loading recipe…</p>
  if (!isNew && recipeError) return <p style={{ padding: 32 }}>Recipe not found.</p>

  const saving = isCreating || isUpdating

  return (
    <section className="page page--recipe-detail">
      <header className="page__header">
        <div>
          <Link to="/recipes" style={{ fontSize: 14 }}>
            ← Recipes
          </Link>
          <h1 style={{ margin: '4px 0 0' }}>{isNew ? 'New recipe' : recipe?.name ?? 'Recipe'}</h1>
        </div>
        {!isNew && (
          <button
            type="button"
            className="btn btn--outline"
            onClick={handleDelete}
            disabled={isDeleting}
          >
            🗑️ Delete
          </button>
        )}
      </header>

      {formError && <div className="error-banner">{formError}</div>}

      <form onSubmit={onSave} noValidate style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div>
          <label htmlFor="recipe-name" style={{ display: 'block', marginBottom: 4, fontWeight: 500 }}>
            Name <span className="field-required">*</span>
          </label>
          <input
            id="recipe-name"
            type="text"
            aria-invalid={errors.name ? 'true' : 'false'}
            {...register('name', {
              required: 'กรุณากรอกชื่อ recipe',
              maxLength: { value: 200, message: 'ยาวเกิน 200 ตัวอักษร' },
              validate: (v) => v.trim().length > 0 || 'กรุณากรอกชื่อ recipe',
            })}
            style={{
              width: '100%',
              padding: 10,
              border: '1px solid var(--color-border)',
              borderRadius: 6,
              font: 'inherit',
            }}
          />
          {errors.name && <p className="field-error">{errors.name.message}</p>}
        </div>

        <div>
          <label
            htmlFor="recipe-desc"
            style={{ display: 'block', marginBottom: 4, fontWeight: 500 }}
          >
            Description <span style={{ color: 'var(--color-text-muted)' }}>(optional)</span>
          </label>
          <textarea
            id="recipe-desc"
            rows={3}
            {...register('description', {
              maxLength: { value: 4000, message: 'ยาวเกิน 4000 ตัวอักษร' },
            })}
            style={{
              width: '100%',
              padding: 10,
              border: '1px solid var(--color-border)',
              borderRadius: 6,
              font: 'inherit',
              resize: 'vertical',
            }}
          />
          {errors.description && (
            <p className="field-error">{errors.description.message}</p>
          )}
        </div>

        <div>
          <label style={{ display: 'block', marginBottom: 4, fontWeight: 500 }}>
            Ingredients <span className="field-required">*</span>
          </label>

          {fields.length > 0 && (
            <table className="data-table" style={{ marginBottom: 12 }}>
              <thead>
                <tr>
                  <th>Name</th>
                  <th style={{ width: 160 }}>Quantity *</th>
                  <th style={{ width: 80 }}>Unit</th>
                  <th style={{ width: 60 }}></th>
                </tr>
              </thead>
              <tbody>
                {fields.map((field, index) => {
                  const lineErrors = errors.lines?.[index]
                  return (
                    <tr key={field.id}>
                      <td>{field.ingredientName}</td>
                      <td>
                        <input
                          type="number"
                          min="0"
                          step="any"
                          aria-invalid={lineErrors?.quantity ? 'true' : 'false'}
                          {...register(`lines.${index}.quantity` as const, {
                            required: 'กรอกจำนวน',
                            validate: (v) =>
                              Number(v) > 0 || 'ต้องเป็นเลขบวก',
                          })}
                        />
                        {lineErrors?.quantity && (
                          <p className="field-error">{lineErrors.quantity.message}</p>
                        )}
                      </td>
                      <td>{field.unit}</td>
                      <td>
                        <button
                          type="button"
                          className="btn btn--outline btn--sm"
                          onClick={() => remove(index)}
                        >
                          ✕
                        </button>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          )}

          <div className="row-add" style={{ alignItems: 'flex-start' }}>
            <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 4 }}>
              <input
                list="ingredient-options"
                type="text"
                placeholder="Ingredient name"
                value={pickerName}
                onChange={(e) => {
                  setPickerName(e.target.value)
                  setPickerError(null)
                }}
                disabled={isLoadingIngredients}
              />
            </div>
            <div style={{ width: 140, display: 'flex', flexDirection: 'column', gap: 4 }}>
              <input
                type="text"
                placeholder="Unit (if new)"
                value={pickerUnit}
                onChange={(e) => setPickerUnit(e.target.value)}
              />
            </div>
            <button type="button" className="btn btn--outline" onClick={handleAddLine}>
              + add
            </button>
          </div>
          {pickerError && <p className="field-error">{pickerError}</p>}
          <datalist id="ingredient-options">
            {availableIngredients.map((i) => (
              <option key={i.id} value={i.name} label={i.unit} />
            ))}
          </datalist>
          <p style={{ fontSize: 12, color: 'var(--color-text-muted)', marginTop: 4 }}>
            เลือกจาก master list หรือพิมพ์ชื่อใหม่ + หน่วย เพื่อสร้างทันที
          </p>
        </div>

        <div style={{ display: 'flex', gap: 8 }}>
          <button type="submit" className="btn btn--primary" disabled={saving}>
            {saving ? 'Saving…' : isNew ? 'Create' : 'Save'}
          </button>
          <Link to="/recipes" className="btn btn--outline">
            Cancel
          </Link>
        </div>
      </form>
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
