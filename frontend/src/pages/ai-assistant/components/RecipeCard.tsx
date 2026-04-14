import { useNavigate } from 'react-router-dom'
import { Button, Color, Variant } from '@syncfusion/react-buttons'

interface RecipeCardProps {
  recipeId: string
  name: string
  stockMatch?: string
}

export function RecipeCard({ recipeId, name, stockMatch }: RecipeCardProps) {
  const navigate = useNavigate()
  return (
    <div className="ai-recipe-card">
      <div className="ai-recipe-card__header">
        <span className="ai-recipe-card__name">{name}</span>
        {stockMatch && <span className="ai-recipe-card__stock">{stockMatch}</span>}
      </div>
      <div className="ai-recipe-card__actions">
        <Button variant={Variant.Outlined} color={Color.Primary} onClick={() => navigate(`/recipes/${recipeId}`)}>
          ดูสูตร
        </Button>
      </div>
    </div>
  )
}
