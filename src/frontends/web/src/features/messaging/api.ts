import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState } from 'react';
import type { Conversation, Message, Page } from '@/shared/api/contracts';
import { ApiError, getJson, request } from '@/shared/api/http';

export const conversationKeys = {
  list: ['conversations', 'list'] as const,
  detail: (id: string) => ['conversations', 'detail', id] as const,
};

export function useConversations(enabled = true) {
  return useQuery({
    queryKey: conversationKeys.list,
    enabled,
    refetchInterval: 30_000,
    queryFn: async ({ signal }) => {
      const items: Conversation[] = [];
      let cursor: string | null = null;
      for (let i = 0; i < 10; i++) {
        const page: Page<Conversation> = await getJson<Page<Conversation>>('/api/v1/conversations', { query: { limit: 100, cursor }, signal });
        items.push(...page.items);
        if (!page.hasMore) break;
        cursor = page.nextCursor;
      }
      return items;
    },
  });
}

export function useConversation(id: string) {
  return useQuery({
    queryKey: conversationKeys.detail(id),
    refetchInterval: 15_000,
    queryFn: ({ signal }) => getJson<Conversation>(`/api/v1/conversations/${id}`, { signal }),
  });
}

const POLL_MS = 4000;

interface HistoryState {
  messages: Message[];
  status: 'loading' | 'ready' | 'error';
  error: unknown;
}

/**
 * Histórico incremental: lê todas as páginas pela sequência monotônica e depois consulta
 * novidades a partir do resumeCursor, sem transporte em tempo real (decisão 0010).
 */
export function useMessageHistory(conversationId: string) {
  const [state, setState] = useState<HistoryState>({ messages: [], status: 'loading', error: null });
  const resume = useRef<string | null>(null);
  const busy = useRef(false);

  const merge = useCallback((incoming: Message[]) => {
    if (incoming.length === 0) return;
    setState((current) => {
      const known = new Map(current.messages.map((m) => [m.id, m]));
      incoming.forEach((m) => known.set(m.id, m));
      const messages = [...known.values()].sort((a, b) => a.sequence - b.sequence);
      return { ...current, messages };
    });
  }, []);

  const pull = useCallback(
    async (signal: AbortSignal) => {
      if (busy.current) return;
      busy.current = true;
      try {
        let cursor = resume.current;
        for (let i = 0; i < 50; i++) {
          const page = await getJson<Page<Message>>(`/api/v1/conversations/${conversationId}/messages`, { query: { limit: 100, cursor }, signal });
          merge(page.items);
          if (page.resumeCursor) resume.current = page.resumeCursor;
          if (!page.hasMore || !page.nextCursor) break;
          cursor = page.nextCursor;
        }
        setState((current) => ({ ...current, status: 'ready', error: null }));
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') return;
        // Falha transitória ou sessão expirada (o aviso de novo login já aparece) mantêm o histórico carregado.
        const keep = error instanceof ApiError && (error.isTransient || error.isSessionExpired);
        setState((current) => ({ ...current, status: current.status === 'ready' && keep ? 'ready' : 'error', error }));
      } finally {
        busy.current = false;
      }
    },
    [conversationId, merge],
  );

  useEffect(() => {
    const controller = new AbortController();
    resume.current = null;
    void pull(controller.signal);
    const timer = window.setInterval(() => void pull(controller.signal), POLL_MS);
    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, [pull]);

  const refresh = useCallback(() => {
    setState((current) => ({ ...current, status: current.messages.length ? 'ready' : 'loading' }));
    void pull(new AbortController().signal);
  }, [pull]);

  return { ...state, merge, refresh };
}

export function useSendMessage(conversationId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ body, key }: { body: string; key: string }) =>
      request<Message>(`/api/v1/conversations/${conversationId}/messages`, { method: 'POST', body: { body }, idempotencyKey: key }).then((r) => r.data),
    onSettled: () => {
      void client.invalidateQueries({ queryKey: conversationKeys.list });
      void client.invalidateQueries({ queryKey: conversationKeys.detail(conversationId) });
    },
  });
}
