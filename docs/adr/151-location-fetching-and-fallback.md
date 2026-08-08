# Location Fetching and Fallback

```mermaid
flowchart TD
    Q{How to fetch user location and handle denial?} -->|chosen| A["Use navigator.geolocation, fallback to manual search input"]
    Q -->|rejected| B["Use 3rd-party IP-based geolocation, less accurate and adds cost"]
    Q -->|rejected| C["Fail silently or disable distance feature, breaks core value proposition"]
```

We will use the standard HTML5 `navigator.geolocation` API to fetch the user's current location, as it provides the most accurate GPS coordinates required for distance calculation. If the user denies permission, the UI will display a warning and a manual search input to let them specify their starting point, ensuring the distance calculation feature can still function.
