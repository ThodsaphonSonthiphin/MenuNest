# Review links are entered at Capture, via the shared preview card

```mermaid
flowchart TD
    Q{"Where can a TikTok/Review link be attached when capturing a new place?"} -->|chosen| A["Add a Review-links section to the SHARED AddPlacePreviewCard — available from the itinerary flow AND the Places-tab Capture"]
    Q -->|rejected| B["Only in the itinerary add-new-place flow — Places-tab Capture still needs capture-then-edit"]
```

`addTripPlace` already accepts `reviewLinks`; today the Capture card hard-codes `[]`. We add a
**Review link** section (reusing `ReviewLinksSection` + the `reviewLinks` lib) to the *shared*
`AddPlacePreviewCard`, so any **Capture** — itinerary or Places tab — can attach review links
in one step, closing the existing "capture, then re-open the Stop/Place editor to add the link"
gap everywhere rather than only on the new path. The links belong to the **Place** (ADR-049),
never the **Stop**. See [[067]], [[project_trip_review_link]].
