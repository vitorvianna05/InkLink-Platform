import { http, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { problem } from '@/test/render';
import { server } from '@/test/server';
import { ApiError, onSessionExpired, request, resetCsrfToken } from './http';

describe('cliente HTTP', () => {
  it('envia token antiforgery e Idempotency-Key nas escritas', async () => {
    resetCsrfToken();
    let headers: Headers | null = null;
    server.use(
      http.get('/bff/antiforgery', () => HttpResponse.json({ requestToken: 'abc' })),
      http.post('/api/v1/conversations/1/messages', ({ request: req }) => {
        headers = req.headers;
        return HttpResponse.json({ id: 'm1' }, { status: 201 });
      }),
    );
    await request('/api/v1/conversations/1/messages', { method: 'POST', body: { body: 'Oi' }, idempotencyKey: 'key-1' });
    expect(headers!.get('X-CSRF-TOKEN')).toBe('abc');
    expect(headers!.get('Idempotency-Key')).toBe('key-1');
  });

  it('não envia token antiforgery em leituras', async () => {
    let headers: Headers | null = null;
    server.use(
      http.get('/api/v1/artists', ({ request: req }) => {
        headers = req.headers;
        return HttpResponse.json({ items: [] });
      }),
    );
    await request('/api/v1/artists', { query: { limit: 6, cursor: null } });
    expect(headers!.get('X-CSRF-TOKEN')).toBeNull();
  });

  it('interpreta Problem Details com code, traceId e erros por campo', async () => {
    server.use(http.get('/api/v2/engagements/x', () => problem(400, 'validation', { errors: { idea: ['Obrigatório'] } })));
    const error = await request('/api/v2/engagements/x').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe('validation');
    expect((error as ApiError).traceId).toBe('trace-1');
    expect((error as ApiError).fieldErrors.idea).toEqual(['Obrigatório']);
  });

  it('renova o token antiforgery uma vez quando o BFF o rejeita', async () => {
    resetCsrfToken();
    let tokens = 0;
    const sent: string[] = [];
    server.use(
      http.get('/bff/antiforgery', () => HttpResponse.json({ requestToken: `t${++tokens}` })),
      http.put('/api/v1/favorites/a', ({ request: req }) => {
        sent.push(req.headers.get('X-CSRF-TOKEN') ?? '');
        return sent.length === 1 ? problem(400, 'csrf.invalid') : new HttpResponse(null, { status: 204 });
      }),
    );
    const result = await request('/api/v1/favorites/a', { method: 'PUT' });
    expect(result.status).toBe(204);
    expect(sent).toEqual(['t1', 't2']);
  });

  it('avisa a sessão quando qualquer chamada recebe 401', async () => {
    const listener = vi.fn();
    const off = onSessionExpired(listener);
    server.use(http.get('/api/v1/favorites', () => problem(401, 'session.required')));
    await expect(request('/api/v1/favorites')).rejects.toMatchObject({ status: 401 });
    expect(listener).toHaveBeenCalledOnce();
    off();
  });

  it('trata falha de rede como erro transitório', async () => {
    server.use(http.get('/api/v1/artists', () => HttpResponse.error()));
    const error = (await request('/api/v1/artists').catch((e: unknown) => e)) as ApiError;
    expect(error.status).toBe(0);
    expect(error.isTransient).toBe(true);
  });
});
