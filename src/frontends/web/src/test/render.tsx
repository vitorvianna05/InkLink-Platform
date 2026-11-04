import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router';
import { AppRoutes } from '@/app/App';
import { SessionProvider } from '@/features/session/session';
import type { Profile } from '@/shared/api/contracts';
import { server } from './server';

export function renderApp(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <SessionProvider>
          <AppRoutes />
        </SessionProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

export const problem = (status: number, code: string, extra: Record<string, unknown> = {}) =>
  HttpResponse.json({ status, code, title: code, traceId: 'trace-1', ...extra }, { status, headers: { 'Content-Type': 'application/problem+json' } });

export const client: Profile = { id: '00000000-0000-7000-8000-00000000c11e', displayName: 'Júlia Vieira' };
export const artist: Profile = { id: '00000000-0000-7000-8000-0000000a7157', displayName: 'Rafael Moura' };

/** Sessão anônima: /bff/session responde 401 como o BFF. */
export function anonymous() {
  server.use(http.get('/bff/session', () => problem(401, 'session.required')));
}

/** Sessão autenticada; isArtist define a resposta do contexto da agenda (200 ou 403). */
export function signedIn(profile: Profile, isArtist = false) {
  server.use(
    http.get('/bff/session', () => HttpResponse.json({ authenticated: true, name: profile.displayName })),
    http.get('/bff/antiforgery', () => HttpResponse.json({ requestToken: 'csrf-token' })),
    http.get('/api/v1/profiles/me', () => HttpResponse.json(profile)),
    http.get('/api/v1/scheduling/artist-context', () =>
      isArtist ? HttpResponse.json({ artistId: profile.id, timeZoneId: 'America/Sao_Paulo', observedAt: new Date().toISOString() }) : problem(403, 'authorization.denied'),
    ),
  );
}

export const page = <T,>(items: T[], extra: Record<string, unknown> = {}) => ({ items, nextCursor: null, hasMore: false, total: items.length, ...extra });
