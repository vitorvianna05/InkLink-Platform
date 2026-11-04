import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { Dashboard, EntryInput, LedgerEntry, Page } from '@/shared/api/contracts';
import { getJson, request } from '@/shared/api/http';

export const financeKeys = {
  dashboard: (from: string, to: string) => ['finance', 'dashboard', from, to] as const,
  entries: (from: string, to: string, kind: string) => ['finance', 'entries', from, to, kind] as const,
};

/** Indicadores do período [from, to): falhas de fonte retornam 503 e nunca viram zero. */
export function useDashboard(from: string, to: string, enabled = true) {
  return useQuery({
    queryKey: financeKeys.dashboard(from, to),
    enabled,
    queryFn: ({ signal }) => getJson<Dashboard>('/api/v1/finance/dashboard', { query: { from, to }, signal }),
  });
}

export function useEntries(from: string, to: string, kind: string, enabled = true) {
  return useQuery({
    queryKey: financeKeys.entries(from, to, kind),
    enabled,
    queryFn: async ({ signal }) => {
      const items: LedgerEntry[] = [];
      let cursor: string | null = null;
      for (let i = 0; i < 20; i++) {
        const page: Page<LedgerEntry> = await getJson<Page<LedgerEntry>>('/api/v1/finance/entries', { query: { from, to, kind, limit: 100, cursor }, signal });
        items.push(...page.items);
        if (!page.hasMore) break;
        cursor = page.nextCursor;
      }
      return items;
    },
  });
}

export function useRecordEntry() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ input, key }: { input: EntryInput; key: string }) =>
      request<LedgerEntry>('/api/v1/finance/entries', { method: 'POST', body: input, idempotencyKey: key }).then((r) => r.data),
    onSettled: () => client.invalidateQueries({ queryKey: ['finance'] }),
  });
}

export function useReverseEntry() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, reason, key }: { id: string; reason: string; key: string }) =>
      request<LedgerEntry>(`/api/v1/finance/entries/${id}/reversal`, { method: 'POST', body: { reason }, idempotencyKey: key }).then((r) => r.data),
    onSettled: () => client.invalidateQueries({ queryKey: ['finance'] }),
  });
}
