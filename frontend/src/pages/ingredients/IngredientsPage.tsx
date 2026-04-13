import { useState } from 'react'
import {
  useListIngredientsQuery,
  useCreateIngredientMutation,
  useUpdateIngredientMutation,
  useDeleteIngredientMutation,
} from '../../shared/api/api'
import type { IngredientDto } from '../../shared/api/api'

export function IngredientsPage() {
  const { data, isLoading, error } = useListIngredientsQuery()
  const [createIngredient, { isLoading: isCreating }] = useCreateIngredientMutation()
  const [updateIngredient] = useUpdateIngredientMutation()
  const [deleteIngredient] = useDeleteIngredientMutation()

  const [newName, setNewName] = useState('')
  const [newUnit, setNewUnit] = useState('')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editName, setEditName] = useState('')
  const [editUnit, setEditUnit] = useState('')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault()
    const name = newName.trim()
    const unit = newUnit.trim()
    if (!name || !unit) return
    setErrorMessage(null)
    try {
      await createIngredient({ name, unit }).unwrap()
      setNewName('')
      setNewUnit('')
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const startEdit = (ingredient: IngredientDto) => {
    setEditingId(ingredient.id)
    setEditName(ingredient.name)
    setEditUnit(ingredient.unit)
    setErrorMessage(null)
  }

  const cancelEdit = () => {
    setEditingId(null)
    setEditName('')
    setEditUnit('')
  }

  const saveEdit = async () => {
    if (!editingId) return
    const name = editName.trim()
    const unit = editUnit.trim()
    if (!name || !unit) return
    setErrorMessage(null)
    try {
      await updateIngredient({ id: editingId, name, unit }).unwrap()
      cancelEdit()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleDelete = async (ingredient: IngredientDto) => {
    if (!confirm(`ลบ "${ingredient.name}" หรือไม่?`)) return
    setErrorMessage(null)
    try {
      await deleteIngredient(ingredient.id).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  return (
    <section className="page page--ingredients">
      <header className="page__header">
        <h1>Ingredients</h1>
      </header>

      <p style={{ color: 'var(--color-text-muted)', marginBottom: 16 }}>
        Master list นี้ใช้เป็น autocomplete เวลาสร้าง recipe / เพิ่ม stock — 1 ingredient = 1 หน่วย
      </p>

      <form onSubmit={handleCreate} className="ingredient-add">
        <input
          type="text"
          placeholder="ชื่อวัตถุดิบ (เช่น ไข่ไก่)"
          value={newName}
          onChange={(e) => setNewName(e.target.value)}
          disabled={isCreating}
          maxLength={120}
          required
        />
        <input
          type="text"
          placeholder="หน่วย (เช่น ฟอง)"
          value={newUnit}
          onChange={(e) => setNewUnit(e.target.value)}
          disabled={isCreating}
          maxLength={40}
          required
          style={{ maxWidth: 120 }}
        />
        <button
          type="submit"
          className="btn btn--primary"
          disabled={isCreating || !newName.trim() || !newUnit.trim()}
        >
          {isCreating ? '...' : '+ เพิ่ม'}
        </button>
      </form>

      {errorMessage && (
        <div className="error-banner">{errorMessage}</div>
      )}

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
                      value={editName}
                      onChange={(e) => setEditName(e.target.value)}
                      maxLength={120}
                    />
                  </td>
                  <td>
                    <input
                      value={editUnit}
                      onChange={(e) => setEditUnit(e.target.value)}
                      maxLength={40}
                    />
                  </td>
                  <td>
                    <button type="button" className="btn btn--primary btn--sm" onClick={saveEdit}>
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
