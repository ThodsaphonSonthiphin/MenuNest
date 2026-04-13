import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import {
  useCreateIngredientMutation,
  useCreateRecipeMutation,
  useDeleteRecipeMutation,
  useGetRecipeQuery,
  useListIngredientsQuery,
  useUpdateRecipeMutation,
} from '../../shared/api/api'
import type { RecipeIngredientDto } from '../../shared/api/api'

interface EditableLine {
  ingredientId: string
  ingredientName: string
  unit: string
  quantity: string // string while editing so the user can clear it
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

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [lines, setLines] = useState<EditableLine[]>([])
  const [pickerValue, setPickerValue] = useState('')
  const [pickerUnit, setPickerUnit] = useState('')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  // Populate form from the fetched recipe once it arrives.
  useEffect(() => {
    if (!recipe) return
    setName(recipe.name)
    setDescription(recipe.description ?? '')
    setLines(
      recipe.ingredients.map((ri: RecipeIngredientDto) => ({
        ingredientId: ri.ingredientId,
        ingredientName: ri.ingredientName,
        unit: ri.unit,
        quantity: ri.quantity.toString(),
      })),
    )
  }, [recipe])

  const usedIngredientIds = useMemo(() => new Set(lines.map((l) => l.ingredientId)), [lines])
  const availableIngredients = (masterIngredients ?? []).filter((i) => !usedIngredientIds.has(i.id))

  const handleAddLine = async () => {
    setErrorMessage(null)
    const typed = pickerValue.trim()
    if (!typed) return

    // If the typed value matches an existing master ingredient,
    // attach that one. Otherwise try to create a new master entry
    // on the fly (needs a unit).
    const match = (masterIngredients ?? []).find(
      (i) => i.name.trim().toLowerCase() === typed.toLowerCase(),
    )

    if (match) {
      if (usedIngredientIds.has(match.id)) {
        setErrorMessage(`"${match.name}" already in this recipe.`)
        return
      }
      setLines((l) => [
        ...l,
        { ingredientId: match.id, ingredientName: match.name, unit: match.unit, quantity: '1' },
      ])
      setPickerValue('')
      setPickerUnit('')
      return
    }

    const unit = pickerUnit.trim()
    if (!unit) {
      setErrorMessage(`"${typed}" is new — enter a unit (e.g. ฟอง, กรัม) and click "+ add" again.`)
      return
    }

    try {
      const created = await createIngredient({ name: typed, unit }).unwrap()
      setLines((l) => [
        ...l,
        { ingredientId: created.id, ingredientName: created.name, unit: created.unit, quantity: '1' },
      ])
      setPickerValue('')
      setPickerUnit('')
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleChangeQuantity = (index: number, value: string) => {
    setLines((l) => l.map((line, i) => (i === index ? { ...line, quantity: value } : line)))
  }

  const handleRemoveLine = (index: number) => {
    setLines((l) => l.filter((_, i) => i !== index))
  }

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    setErrorMessage(null)
    const trimmedName = name.trim()
    if (!trimmedName) {
      setErrorMessage('Recipe name is required.')
      return
    }
    const payloadLines = lines.map((l) => ({
      ingredientId: l.ingredientId,
      quantity: Number(l.quantity),
    }))
    if (payloadLines.some((l) => !(l.quantity > 0))) {
      setErrorMessage('Every ingredient quantity must be a positive number.')
      return
    }

    try {
      if (isNew) {
        const created = await createRecipe({
          name: trimmedName,
          description: description.trim() || null,
          ingredients: payloadLines,
        }).unwrap()
        navigate(`/recipes/${created.id}`, { replace: true })
      } else {
        await updateRecipe({
          id: id!,
          name: trimmedName,
          description: description.trim() || null,
          ingredients: payloadLines,
        }).unwrap()
      }
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleDelete = async () => {
    if (!id || isNew) return
    if (!confirm(`ลบ "${name}" หรือไม่?`)) return
    setErrorMessage(null)
    try {
      await deleteRecipe(id).unwrap()
      navigate('/recipes', { replace: true })
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
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
          <h1 style={{ margin: '4px 0 0' }}>{isNew ? 'New recipe' : name || 'Recipe'}</h1>
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

      {errorMessage && <div className="error-banner">{errorMessage}</div>}

      <form onSubmit={handleSave} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div>
          <label htmlFor="recipe-name" style={{ display: 'block', marginBottom: 4, fontWeight: 500 }}>
            Name
          </label>
          <input
            id="recipe-name"
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            maxLength={200}
            required
            style={{
              width: '100%',
              padding: 10,
              border: '1px solid var(--color-border)',
              borderRadius: 6,
              font: 'inherit',
            }}
          />
        </div>

        <div>
          <label htmlFor="recipe-desc" style={{ display: 'block', marginBottom: 4, fontWeight: 500 }}>
            Description <span style={{ color: 'var(--color-text-muted)' }}>(optional)</span>
          </label>
          <textarea
            id="recipe-desc"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            maxLength={4000}
            rows={3}
            style={{
              width: '100%',
              padding: 10,
              border: '1px solid var(--color-border)',
              borderRadius: 6,
              font: 'inherit',
              resize: 'vertical',
            }}
          />
        </div>

        <div>
          <label style={{ display: 'block', marginBottom: 4, fontWeight: 500 }}>Ingredients</label>

          {lines.length > 0 && (
            <table className="data-table" style={{ marginBottom: 12 }}>
              <thead>
                <tr>
                  <th>Name</th>
                  <th style={{ width: 140 }}>Quantity</th>
                  <th style={{ width: 80 }}>Unit</th>
                  <th style={{ width: 60 }}></th>
                </tr>
              </thead>
              <tbody>
                {lines.map((line, index) => (
                  <tr key={line.ingredientId}>
                    <td>{line.ingredientName}</td>
                    <td>
                      <input
                        type="number"
                        min="0"
                        step="any"
                        value={line.quantity}
                        onChange={(e) => handleChangeQuantity(index, e.target.value)}
                      />
                    </td>
                    <td>{line.unit}</td>
                    <td>
                      <button
                        type="button"
                        className="btn btn--outline btn--sm"
                        onClick={() => handleRemoveLine(index)}
                      >
                        ✕
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <div className="row-add" style={{ alignItems: 'flex-end' }}>
            <input
              list="ingredient-options"
              type="text"
              placeholder="Ingredient name"
              value={pickerValue}
              onChange={(e) => setPickerValue(e.target.value)}
              disabled={isLoadingIngredients}
            />
            <input
              type="text"
              placeholder="Unit (if new)"
              value={pickerUnit}
              onChange={(e) => setPickerUnit(e.target.value)}
              style={{ maxWidth: 120 }}
            />
            <button type="button" className="btn btn--outline" onClick={handleAddLine}>
              + add
            </button>
          </div>
          <datalist id="ingredient-options">
            {availableIngredients.map((i) => (
              <option key={i.id} value={i.name} label={i.unit} />
            ))}
          </datalist>
          <p style={{ fontSize: 12, color: 'var(--color-text-muted)', marginTop: 4 }}>
            Pick from the master list, or type a new name + unit to create on the fly.
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
