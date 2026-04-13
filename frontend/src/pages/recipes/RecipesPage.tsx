import { useListRecipesQuery } from '../../shared/api/api'

export function RecipesPage() {
  const { data, isLoading, error } = useListRecipesQuery()

  return (
    <section className="page page--recipes">
      <header className="page__header">
        <h1>Recipes</h1>
        <button type="button" className="btn btn--primary">+ New recipe</button>
      </header>

      {isLoading && <p>Loading…</p>}
      {error && <p>Failed to load recipes.</p>}
      {data && data.length === 0 && <p>No recipes yet — add your first.</p>}

      {data && data.length > 0 && (
        <ul className="recipe-grid">
          {data.map((recipe) => (
            <li key={recipe.id}>
              <strong>{recipe.name}</strong>
              <span>{recipe.ingredientCount} ingredients</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
