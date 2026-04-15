import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Controller, useFieldArray, useForm } from 'react-hook-form'
import { Button, Color, Size, Variant } from '@syncfusion/react-buttons'
import { NumericTextBox, TextArea, TextBox } from '@syncfusion/react-inputs'
import { Autocomplete } from '@syncfusion/react-dropdowns'
import { Grid, Column, Columns } from '@syncfusion/react-grid'
import type { ColumnTemplateProps } from '@syncfusion/react-grid'
import {
  useCreateIngredientMutation,
  useCreateRecipeMutation,
  useDeleteRecipeMutation,
  useGetRecipeQuery,
  useListIngredientsQuery,
  useUpdateRecipeMutation,
} from '../../shared/api/api'
import { useConfirm } from '../../shared/hooks/useConfirm'

interface RecipeFormLine {
  ingredientId: string
  ingredientName: string
  unit: string
  quantity: number | null
}

interface RecipeFormValues {
  name: string
  description: string
  lines: RecipeFormLine[]
}

type RecipeFieldRow = RecipeFormLine & { id: string }

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
  const { confirm } = useConfirm()

  const {
    control,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<RecipeFormValues>({
    defaultValues: { name: '', description: '', lines: [] },
  })
  const { fields, append, remove } = useFieldArray({ control, name: 'lines' })

  const [pickerName, setPickerName] = useState('')
  const [pickerUnit, setPickerUnit] = useState('')
  const [pickerError, setPickerError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  useEffect(() => {
    if (!recipe) return
    reset({
      name: recipe.name,
      description: recipe.description ?? '',
      lines: recipe.ingredients.map((ri) => ({
        ingredientId: ri.ingredientId,
        ingredientName: ri.ingredientName,
        unit: ri.unit,
        quantity: ri.quantity,
      })),
    })
  }, [recipe, reset])

  const usedIngredientIds = useMemo(
    () => new Set(fields.map((f) => f.ingredientId)),
    [fields],
  )
  const availableIngredients = (masterIngredients ?? []).filter(
    (i) => !usedIngredientIds.has(i.ingredientId),
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
      if (usedIngredientIds.has(match.ingredientId)) {
        setPickerError(`"${match.name}" อยู่ใน recipe นี้แล้ว`)
        return
      }
      append({
        ingredientId: match.ingredientId,
        ingredientName: match.name,
        unit: match.unit,
        quantity: 1,
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
        ingredientId: created.ingredientId,
        ingredientName: created.name,
        unit: created.unit,
        quantity: 1,
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
    if (payloadLines.some((l) => !(l.quantity > 0))) {
      setFormError('จำนวนของทุกวัตถุดิบต้องเป็นเลขบวก')
      return
    }

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
    const ok = await confirm({
      title: 'ลบ recipe',
      message: (
        <>
          ลบ recipe <strong>"{recipe?.name ?? ''}"</strong> หรือไม่?
        </>
      ),
      confirmText: 'ลบ',
      destructive: true,
    })
    if (!ok) return
    setFormError(null)
    try {
      await deleteRecipe(id).unwrap()
      navigate('/recipes', { replace: true })
    } catch (err) {
      setFormError(getErrorMessage(err))
    }
  }

  const QuantityTemplate = ({ data: row }: ColumnTemplateProps<RecipeFieldRow>) => {
    const index = fields.findIndex((f) => f.id === row.id)
    const lineErrors = errors.lines?.[index]
    return (
      <div>
        <Controller
          control={control}
          name={`lines.${index}.quantity` as const}
          rules={{
            validate: (v) =>
              (v != null && Number(v) > 0) || 'ต้องเป็นเลขบวก',
          }}
          render={({ field: qField }) => (
            <NumericTextBox
              min={0}
              value={qField.value ?? null}
              onChange={(e) => qField.onChange((e.value as number | null) ?? null)}
            />
          )}
        />
        {lineErrors?.quantity && (
          <p className="field-error">{lineErrors.quantity.message}</p>
        )}
      </div>
    )
  }

  const LineActionsTemplate = ({ data: row }: ColumnTemplateProps<RecipeFieldRow>) => {
    const index = fields.findIndex((f) => f.id === row.id)
    return (
      <Button
        type="button"
        size={Size.Small}
        variant={Variant.Outlined}
        color={Color.Secondary}
        onClick={() => remove(index)}
      >
        ✕
      </Button>
    )
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
          <Button
            variant={Variant.Outlined}
            color={Color.Error}
            onClick={handleDelete}
            disabled={isDeleting}
          >
            🗑️ Delete
          </Button>
        )}
      </header>

      {formError && <div className="error-banner">{formError}</div>}

      <form onSubmit={onSave} noValidate style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div>
          <label htmlFor="recipe-name" style={{ display: 'block', marginBottom: 4, fontWeight: 500 }}>
            Name <span className="field-required">*</span>
          </label>
          <Controller
            control={control}
            name="name"
            rules={{
              required: 'กรุณากรอกชื่อ recipe',
              maxLength: { value: 200, message: 'ยาวเกิน 200 ตัวอักษร' },
              validate: (v) => v.trim().length > 0 || 'กรุณากรอกชื่อ recipe',
            }}
            render={({ field }) => (
              <TextBox
                id="recipe-name"
                value={field.value}
                onChange={(e) => field.onChange(e.value ?? '')}
              />
            )}
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
          <Controller
            control={control}
            name="description"
            rules={{ maxLength: { value: 4000, message: 'ยาวเกิน 4000 ตัวอักษร' } }}
            render={({ field }) => (
              <TextArea
                id="recipe-desc"
                rows={3}
                value={field.value}
                onChange={(e) => field.onChange(e.value ?? '')}
              />
            )}
          />
          {errors.description && <p className="field-error">{errors.description.message}</p>}
        </div>

        <div>
          <label style={{ display: 'block', marginBottom: 4, fontWeight: 500 }}>
            Ingredients <span className="field-required">*</span>
          </label>

          {fields.length > 0 && (
            <div style={{ marginBottom: 12 }}>
              <Grid dataSource={fields as RecipeFieldRow[]} height="auto">
                <Columns>
                  <Column field="ingredientName" headerText="Name" />
                  <Column headerText="Quantity *" width={180} template={QuantityTemplate} />
                  <Column field="unit" headerText="Unit" width={80} />
                  <Column headerText="" width={60} template={LineActionsTemplate} />
                </Columns>
              </Grid>
            </div>
          )}

          <div className="row-add" style={{ alignItems: 'flex-start' }}>
            <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 4, minWidth: 180 }}>
              <Autocomplete
                dataSource={availableIngredients.map((i) => ({
                  id: i.ingredientId,
                  label: i.name,
                  unit: i.unit,
                }))}
                fields={{ text: 'label', value: 'label' }}
                value={pickerName}
                placeholder="Ingredient name"
                disabled={isLoadingIngredients}
                onChange={(e: { value: unknown }) => {
                  setPickerName((e.value as string) ?? '')
                  setPickerError(null)
                }}
              />
            </div>
            <div style={{ width: 140, display: 'flex', flexDirection: 'column', gap: 4 }}>
              <TextBox
                placeholder="Unit (if new)"
                value={pickerUnit}
                onChange={(e) => setPickerUnit(e.value ?? '')}
              />
            </div>
            <Button
              type="button"
              variant={Variant.Outlined}
              color={Color.Primary}
              onClick={handleAddLine}
            >
              + add
            </Button>
          </div>
          {pickerError && <p className="field-error">{pickerError}</p>}
          <p style={{ fontSize: 12, color: 'var(--color-text-muted)', marginTop: 4 }}>
            เลือกจาก master list หรือพิมพ์ชื่อใหม่ + หน่วย เพื่อสร้างทันที
          </p>
        </div>

        <div style={{ display: 'flex', gap: 8 }}>
          <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={saving}>
            {saving ? 'Saving…' : isNew ? 'Create' : 'Save'}
          </Button>
          <Link to="/recipes" style={{ textDecoration: 'none' }}>
            <Button type="button" variant={Variant.Outlined} color={Color.Secondary}>
              Cancel
            </Button>
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
