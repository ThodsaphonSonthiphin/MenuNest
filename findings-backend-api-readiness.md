### Backend API Support Status: Trips Paging, Sorting, and Filtering

**Current Status:** The backend API **DOES NOT** support paging, dynamic sorting, or filtering for Trips.
- **Paging:** Not supported. It fetches all records at once.
- **Sorting:** Not dynamically supported. It is hardcoded to sort by `StartDate` descending.
- **Filtering:** Not supported. There are no filters for Name or Location.

---

### Exact Changes Required

To support Paging (10 items), Sorting (date, name), and Filtering (name, location), the following files must be modified:

#### 1. `backend/src/MenuNest.WebApi/Controllers/TripsController.cs`
Modify the `List` endpoint (`GET /api/trips`) to accept query parameters:
```csharp
[HttpGet("api/trips")]
public async Task<ActionResult<TripsPageDto>> List(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 10,
    [FromQuery] string? sortBy = null,
    [FromQuery] bool sortDesc = false,
    [FromQuery] string? filterName = null,
    [FromQuery] string? filterLocation = null,
    CancellationToken ct = default)
{
    return Ok(await _mediator.Send(new ListTripsQuery(page, pageSize, sortBy, sortDesc, filterName, filterLocation), ct));
}
```

#### 2. `backend/src/MenuNest.Application/UseCases/Trips/ListTrips/ListTripsQuery.cs`
Update the query record to accept the new arguments and change the return type to a paginated DTO:
```csharp
public sealed record ListTripsQuery(
    int Page,
    int PageSize,
    string? SortBy,
    bool SortDesc,
    string? FilterName,
    string? FilterLocation
) : IQuery<TripsPageDto>;
```

#### 3. Create/Update DTO for Paged Results
*(Likely in `backend/src/MenuNest.Application/UseCases/Trips/TripDtos.cs`)*
Create a container for the paginated result:
```csharp
public sealed record TripsPageDto(
    IReadOnlyList<TripDto> Items,
    int TotalCount
);
```

#### 4. `backend/src/MenuNest.Application/UseCases/Trips/ListTrips/ListTripsHandler.cs`
Update the handler to apply the new logic:
- Update the handler signature to implement `IQueryHandler<ListTripsQuery, TripsPageDto>`.
- **Apply Filtering:** 
  ```csharp
  var query = _db.Trips.Where(t => t.UserId == user.Id && t.DeletedAt == null);
  
  if (!string.IsNullOrWhiteSpace(q.FilterName))
      query = query.Where(t => t.Name.Contains(q.FilterName));
      
  if (!string.IsNullOrWhiteSpace(q.FilterLocation))
      query = query.Where(t => t.Destination != null && t.Destination.Contains(q.FilterLocation));
  ```
- **Apply Sorting:** 
  ```csharp
  query = q.SortBy?.ToLower() switch
  {
      "name" => q.SortDesc ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name),
      _ => q.SortDesc ? query.OrderByDescending(t => t.StartDate) : query.OrderBy(t => t.StartDate)
  };
  ```
- **Apply Paging and execute:**
  ```csharp
  var totalCount = await query.CountAsync(ct);
  
  var items = await query
      .Skip((q.Page - 1) * q.PageSize)
      .Take(q.PageSize)
      .Select(t => new TripDto(t.Id, t.Name, t.Destination, t.StartDate, t.DayCount, t.DefaultTravelMode, t.IsDaily))
      .ToListAsync(ct);
      
  return new TripsPageDto(items, totalCount);
  ```
