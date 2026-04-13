import { useParams } from 'react-router-dom'

export function RecipeDetailPage() {
  const { id } = useParams<{ id: string }>()
  const isNew = id === 'new'

  return (
    <section className="page page--recipe-detail">
      <h1>{isNew ? 'New recipe' : 'Recipe'}</h1>
      <p>Detail / edit form placeholder (id: {id}).</p>
    </section>
  )
}
