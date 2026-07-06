import { useQuery } from '@tanstack/react-query'

import { getHealth } from '@/lib/api/client'

// Server capabilities probed from /healthz. They do not change within a session,
// so the query never refetches; only the host needs it (to decide whether the
// finalize dialog offers the summary email field).
export function useCapabilities(enabled: boolean): { emailEnabled: boolean } {
  const query = useQuery({
    queryKey: ['healthz'],
    queryFn: getHealth,
    enabled,
    staleTime: Infinity,
    retry: false,
  })

  return { emailEnabled: query.data?.email ?? false }
}
