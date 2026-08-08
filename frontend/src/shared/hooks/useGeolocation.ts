import { useState, useEffect } from 'react'
import type { LatLng } from '../utils/distance'

export interface GeolocationState {
  location: LatLng | null
  loading: boolean
  error: GeolocationPositionError | null
}

export function useGeolocation(options?: PositionOptions) {
  const [state, setState] = useState<GeolocationState>({
    location: null,
    loading: true,
    error: null,
  })

  useEffect(() => {
    if (!('geolocation' in navigator)) {
      setState((s) => ({ 
        ...s, 
        loading: false, 
        error: { 
          code: 0, 
          message: 'Geolocation not supported', 
          PERMISSION_DENIED: 1, 
          POSITION_UNAVAILABLE: 2, 
          TIMEOUT: 3 
        } as GeolocationPositionError 
      }))
      return
    }

    const successHandler = (pos: GeolocationPosition) => {
      const newLat = Math.round(pos.coords.latitude * 10000) / 10000
      const newLng = Math.round(pos.coords.longitude * 10000) / 10000

      setState((prev) => {
        if (prev.location?.lat === newLat && prev.location?.lng === newLng && !prev.loading && !prev.error) {
          return prev // Same location, no state change
        }
        return {
          location: { lat: newLat, lng: newLng },
          loading: false,
          error: null,
        }
      })
    }

    const errorHandler = (err: GeolocationPositionError) => {
      setState((s) => ({ ...s, loading: false, error: err }))
    }

    // Try to get immediate position
    navigator.geolocation.getCurrentPosition(successHandler, errorHandler, options)

    // Watch for updates
    const watchId = navigator.geolocation.watchPosition(successHandler, errorHandler, options)

    return () => {
      navigator.geolocation.clearWatch(watchId)
    }
  }, [options])

  return state
}
