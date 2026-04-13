using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A recipe the family has saved: name, optional description, optional
/// photo, and a list of <see cref="RecipeIngredient"/> lines. The
/// recipe is the aggregate root for its ingredients — callers mutate
/// lines through methods on Recipe, not on the lines directly.
/// </summary>
public sealed class Recipe : Entity
{
    public Guid FamilyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? ImageBlobPath { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    private readonly List<RecipeIngredient> _ingredients = new();
    public IReadOnlyCollection<RecipeIngredient> Ingredients => _ingredients.AsReadOnly();

    // EF Core
    private Recipe() { }

    public static Recipe Create(
        Guid familyId,
        string name,
        Guid createdByUserId,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Recipe name is required.");
        }

        return new Recipe
        {
            FamilyId = familyId,
            Name = name.Trim(),
            Description = description?.Trim(),
            CreatedByUserId = createdByUserId
        };
    }

    public void UpdateDetails(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Recipe name is required.");
        }

        Name = name.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetImage(string blobPath)
    {
        if (string.IsNullOrWhiteSpace(blobPath))
        {
            throw new DomainException("Image blob path cannot be empty.");
        }

        ImageBlobPath = blobPath;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RemoveImage()
    {
        ImageBlobPath = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public RecipeIngredient AddIngredient(Guid ingredientId, decimal quantity)
    {
        if (_ingredients.Any(i => i.IngredientId == ingredientId))
        {
            throw new DomainException("This ingredient is already part of the recipe.");
        }

        var line = RecipeIngredient.Create(Id, ingredientId, quantity);
        _ingredients.Add(line);
        UpdatedAt = DateTime.UtcNow;
        return line;
    }

    public void UpdateIngredientQuantity(Guid ingredientId, decimal quantity)
    {
        var line = _ingredients.FirstOrDefault(i => i.IngredientId == ingredientId)
            ?? throw new DomainException("Ingredient is not part of this recipe.");

        line.UpdateQuantity(quantity);
        UpdatedAt = DateTime.UtcNow;
    }

    public void RemoveIngredient(Guid ingredientId)
    {
        var line = _ingredients.FirstOrDefault(i => i.IngredientId == ingredientId);
        if (line is null) return;

        _ingredients.Remove(line);
        UpdatedAt = DateTime.UtcNow;
    }
}
