import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { Availability, Page, Reservation, SessionCompletion, TimeWindow } from '@/shared/api/contracts';
import { ApiError, getJson, request } from '@/shared/api/http';

export type CalendarPerspective = 'client' | 'artist';

export const schedulingKeys = {
  calendar: (perspective: CalendarPerspective, from: string, to: string) => ['scheduling', 'calendar', perspective, from, to] as const,
  availability: (date: string) => ['scheduling', 'availability', date] as const,
  engagementAvailability: (engagementId: string, date: string) => ['scheduling', 'availability', engagementId, date] as const,
  completion: (reservationId: string) => ['scheduling', 'completion', reservationId] as const,
};

/** Reservas no intervalo [from, to) (máximo de 93 dias por consulta), em ordem de início. */
export function useCalendar(perspective: CalendarPerspective, from: string, to: string, enabled = true) {
  return useQuery({
    queryKey: schedulingKeys.calendar(perspective, from, to),
    enabled,
    queryFn: async ({ signal }) => {
      const items: Reservation[] = [];
      let cursor: string | null = null;
      for (let i = 0; i < 20; i++) {
        const page: Page<Reservation> = await getJson<Page<Reservation>>('/api/v1/scheduling/calendar', {
          query: { from, to, perspective, limit: 100, cursor },
          signal,
        });
        items.push(...page.items);
        if (!page.hasMore) break;
        cursor = page.nextCursor;
      }
      return items.sort((a, b) => a.start.localeCompare(b.start));
    },
  });
}

/** Disponibilidade própria do artista em uma data. */
export function useOwnAvailability(date: string) {
  return useQuery({
    queryKey: schedulingKeys.availability(date),
    queryFn: ({ signal }) => getJson<Availability>(`/api/v1/scheduling/availability/${date}`, { signal }),
  });
}

/** Disponibilidade do artista de um atendimento (consulta não garante a reserva). */
export function useEngagementAvailability(engagementId: string, date: string, enabled = true) {
  return useQuery({
    queryKey: schedulingKeys.engagementAvailability(engagementId, date),
    enabled: enabled && Boolean(engagementId),
    queryFn: ({ signal }) => getJson<Availability>(`/api/v1/scheduling/engagements/${engagementId}/availability/${date}`, { signal }),
  });
}

export function useDefineAvailability(date: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ windows, timeZoneId }: { windows: TimeWindow[]; timeZoneId?: string }) =>
      request<Availability>(`/api/v1/scheduling/availability/${date}`, { method: 'PUT', body: { windows, timeZoneId } }).then((r) => r.data),
    onSuccess: (data) => client.setQueryData(schedulingKeys.availability(date), data),
    onSettled: () => client.invalidateQueries({ queryKey: ['scheduling', 'availability'] }),
  });
}

/**
 * Confirma a reserva (somente o artista). Em timeout, a mesma operação é consultada por
 * operationId antes de qualquer nova tentativa, evitando reservas duplicadas.
 */
export function useConfirmReservation() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async ({ engagementId, start, end, operationId }: { engagementId: string; start: string; end: string; operationId: string }) => {
      try {
        return (await request<Reservation>('/api/v1/scheduling/reservations', { method: 'POST', body: { engagementId, start, end }, idempotencyKey: operationId })).data;
      } catch (error) {
        if (error instanceof ApiError && error.isTransient) {
          try {
            return await getJson<Reservation>(`/api/v1/scheduling/operations/${operationId}`);
          } catch {
            throw error;
          }
        }
        throw error;
      }
    },
    onSettled: () => {
      void client.invalidateQueries({ queryKey: ['scheduling'] });
      void client.invalidateQueries({ queryKey: ['engagements'] });
      void client.invalidateQueries({ queryKey: ['conversations'] });
    },
  });
}

export function useCompletion(reservationId: string, enabled: boolean) {
  return useQuery({
    queryKey: schedulingKeys.completion(reservationId),
    enabled,
    retry: false,
    queryFn: async ({ signal }) => {
      try {
        return await getJson<SessionCompletion>(`/api/v1/scheduling/reservations/${reservationId}/completion`, { signal });
      } catch (error) {
        if (error instanceof ApiError && error.status === 404) return null;
        throw error;
      }
    },
  });
}

export function useCompleteSession(reservationId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: { completedAt: string; amount?: number }) =>
      request<SessionCompletion>(`/api/v1/scheduling/reservations/${reservationId}/completion`, { method: 'PUT', body: input }).then((r) => r.data),
    onSuccess: (data) => client.setQueryData(schedulingKeys.completion(reservationId), data),
    onSettled: () => void client.invalidateQueries({ queryKey: ['finance'] }),
  });
}
