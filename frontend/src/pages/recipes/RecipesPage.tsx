import { Link, useNavigate } from 'react-router-dom'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { TextBox } from '@syncfusion/react-inputs'
import { useListRecipesQuery } from '../../shared/api/api'
import { useAppDispatch, useAppSelector } from '../../store'
import { setSearchTerm } from './recipesSlice'

export function RecipesPage() {
  const navigate = useNavigate()
  const { data, isLoading, error } = useListRecipesQuery()
  const searchTerm = useAppSelector((s) => s.recipes.searchTerm)
  const dispatch = useAppDispatch()

  const filtered = (data ?? []).filter((r) =>
    r.name.toLowerCase().includes(searchTerm.trim().toLowerCase()),
  )

  return (
    <section className="page page--recipes">
      <header className="page__header">
        <h1>Recipes</h1>
        <Button
          variant={Variant.Filled}
          color={Color.Primary}
          onClick={() => navigate('/recipes/new')}
        >
          + New recipe
        </Button>
      </header>

      <div style={{ marginBottom: 16, maxWidth: 400 }}>
        <TextBox
          placeholder="🔍 ค้นหา recipe..."
          value={searchTerm}
          onChange={(e) => dispatch(setSearchTerm(e.value ?? ''))}
        />
      </div>

      {isLoading && <p>Loading…</p>}
      {error && !isLoading && <p>Failed to load recipes.</p>}
      {data && data.length === 0 && !isLoading && (
        <p style={{ textAlign: 'center', padding: 32, color: 'var(--color-text-muted)' }}>
          ยังไม่มี recipe — <Link to="/recipes/new">เพิ่มสูตรแรก</Link>
        </p>
      )}
      {data && data.length > 0 && filtered.length === 0 && (
        <p style={{ textAlign: 'center', padding: 32, color: 'var(--color-text-muted)' }}>
          ไม่พบ recipe ที่ตรงกับ "{searchTerm}"
        </p>
      )}

      {filtered.length > 0 && (
        <ul className="recipe-grid">
          {filtered.map((recipe) => (
            <li key={recipe.id}>
              <Link to={`/recipes/${recipe.id}`} className="recipe-card">
                <div className="recipe-card__image">🍽️</div>
                <div className="recipe-card__body">
                  <h3>{recipe.name}</h3>
                  {recipe.description && (
                    <p className="recipe-card__desc">{recipe.description}</p>
                  )}
                  <div className="recipe-card__meta">
                    {recipe.ingredientCount} ingredients
                  </div>
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
