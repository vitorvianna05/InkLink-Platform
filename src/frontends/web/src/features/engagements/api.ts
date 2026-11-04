import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { Engagement, EngagementRequest, LifecycleOperation, LifecycleStatus, Page, Proposal } from '@/shared/api/contracts';
import { getJson, request } from '@/shared/api/http';

export type Perspective = 'client' | 'artist';

export const engagementKeys = {
  list: (perspective: Perspective) => ['engagements', 'list', perspective] as const,
  detail: (id: string) => ['engagements', 'detail', id] as const,
  proposals: (id: string) => ['engagements', 'detail', id, 'proposals'] as const,
  lifecycle: (id: string) => ['engagements', 'detail', id, 'lifecycle'] as const,
};

async function listAll(perspective: Perspective, signal?: AbortSignal): Promise<Engagement[]> {
  const items: Engagement[] = [];
  let cursor: string | null = null;
  for (let i = 0; i < 10; i++) {
    const page: Page<Engagement> = await getJson<Page<Engagement>>('/api/v2/engagements', { query: { perspective, limit: 100, cursor }, signal });
    items.push(...page.items);
    if (!page.hasMore) break;
    cursor = page.nextCursor;
  }
  return items;
}

/** Atendimentos da conta na perspectiva informada (cliente ou artista). */
export function useEngagements(perspective: Perspective, enabled = true) {
  return useQuery({ queryKey: engagementKeys.list(perspective), enabled, queryFn: ({ signal }) => listAll(perspective, signal) });
}

export function useEngagement(id: string) {
  return useQuery({ queryKey: engagementKeys.detail(id), queryFn: ({ signal }) => getJson<Engagement>(`/api/v2/engagements/${id}`, { signal }) });
}

export function useProposals(id: string) {
  return useQuery({
    queryKey: engagementKeys.proposals(id),
    queryFn: ({ signal }) => getJson<Page<Proposal>>(`/api/v2/engagements/${id}/proposals`, { query: { limit: 100 }, signal }),
  });
}

/** Estado do ciclo (encerramento/ativação); atualiza enquanto houver operação pendente. */
export function useLifecycle(id: string) {
  return useQuery({
    queryKey: engagementKeys.lifecycle(id),
    queryFn: ({ signal }) => getJson<LifecycleStatus>(`/api/v2/engagements/${id}/lifecycle`, { signal }),
    refetchInterval: (query) => (query.state.data?.operations.some((op) => op.status === 'Pending') || query.state.data?.state === 'Closing' ? 3000 : false),
  });
}

export function useRequestEngagement() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ input, key }: { input: EngagementRequest; key: string }) =>
      request<Engagement>('/api/v2/engagements', { method: 'POST', body: input, idempotencyKey: key }).then((r) => r.data),
    onSuccess: (engagement) => {
      client.setQueryData(engagementKeys.detail(engagement.id), engagement);
      void client.invalidateQueries({ queryKey: ['engagements', 'list'] });
      void client.invalidateQueries({ queryKey: ['conversations'] });
    },
  });
}

export function usePresentProposal(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ description, amount, key }: { description: string; amount: number; key: string }) =>
      request<Proposal>(`/api/v2/engagements/${id}/proposals`, { method: 'POST', body: { description, amount }, idempotencyKey: key }).then((r) => r.data),
    onSettled: () => client.invalidateQueries({ queryKey: ['engagements'] }),
  });
}

export function useAcceptProposal(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (proposalId: string) => request<Proposal>(`/api/v2/engagements/${id}/proposals/${proposalId}/acceptance`, { method: 'PUT' }).then((r) => r.data),
    onSettled: () => {
      void client.invalidateQueries({ queryKey: ['engagements'] });
      void client.invalidateQueries({ queryKey: ['conversations'] });
    },
  });
}

/** Solicita o encerramento: 200 = concluído; 202 = processo pendente acompanhado pelo lifecycle. */
export function useCloseEngagement(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => request<LifecycleOperation>(`/api/v2/engagements/${id}/closure`, { method: 'POST', body: { confirmed: true } }),
    onSettled: () => {
      void client.invalidateQueries({ queryKey: ['engagements'] });
      void client.invalidateQueries({ queryKey: ['conversations'] });
    },
  });
}

export function useRetryLifecycle(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => request(`/api/v2/engagements/${id}/lifecycle/retry`, { method: 'POST' }),
    onSettled: () => client.invalidateQueries({ queryKey: engagementKeys.lifecycle(id) }),
  });
}
