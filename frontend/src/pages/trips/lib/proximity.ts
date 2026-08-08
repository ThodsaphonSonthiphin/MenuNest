export type ProximityStatus = 'Arrived' | 'Approaching' | 'EnRoute' | 'Unknown'

/**
 * Returns the proximity status based on distance in kilometers.
 * - Arrived: <= 50 meters (0.05 km)
 * - Approaching: <= 100 meters (0.10 km)
 * - EnRoute: > 100 meters
 * Uses strict stateless toggling (no hysteresis).
 */
export function getProximityStatus(distanceKm: number | null | undefined): ProximityStatus {
  if (distanceKm == null) return 'Unknown'
  if (distanceKm <= 0.05) return 'Arrived'
  if (distanceKm <= 0.10) return 'Approaching'
  return 'EnRoute'
}
