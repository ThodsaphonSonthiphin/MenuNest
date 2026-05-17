import { useCallback } from 'react'
import {
  useCreateShareLinkMutation,
  useListMyShareLinksQuery,
  useRevokeShareLinkMutation,
} from '../../../shared/api/api'
import type {
  CreateShareLinkRequest,
  CreateShareLinkResultDto,
  ShareLinkSummaryDto,
} from '../../../shared/api/healthTypes'

/**
 * Combines the three share-link endpoints into a single hook that the
 * Share Links page can use without juggling three RTKQ states. The
 * `create` action returns the freshly minted `CreateShareLinkResultDto`
 * so the page can render a QR-code modal immediately after the call.
 *
 * `links` is normalized to an empty array so callers don't need to
 * guard the loading state.
 */
export interface UseShareLinksResult {
  links: ShareLinkSummaryDto[]
  isLoading: boolean
  isError: boolean
  create: (args: CreateShareLinkRequest) => Promise<CreateShareLinkResultDto>
  revoke: (id: string) => Promise<void>
  isCreating: boolean
  isRevoking: boolean
}

export function useShareLinks(): UseShareLinksResult {
  const listQuery = useListMyShareLinksQuery()
  const [createMutation, createState] = useCreateShareLinkMutation()
  const [revokeMutation, revokeState] = useRevokeShareLinkMutation()

  const create = useCallback(
    async (args: CreateShareLinkRequest) => {
      return await createMutation(args).unwrap()
    },
    [createMutation],
  )

  const revoke = useCallback(
    async (id: string) => {
      await revokeMutation(id).unwrap()
    },
    [revokeMutation],
  )

  return {
    links: listQuery.data ?? [],
    isLoading: listQuery.isLoading,
    isError: !!listQuery.error,
    create,
    revoke,
    isCreating: createState.isLoading,
    isRevoking: revokeState.isLoading,
  }
}
