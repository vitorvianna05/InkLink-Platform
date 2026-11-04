import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { anonymous, artist, client, page, problem, renderApp, signedIn } from '@/test/render';
import { server } from '@/test/server';

describe('sessão e áreas por função', () => {
  it('visitante vê o convite para entrar nas áreas privadas', async () => {
    anonymous();
    renderApp('/meu-espaco');
    expect(await screen.findByText('Entre para ver seus pedidos, conversas e favoritos.')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Entrar' }).length).toBeGreaterThan(0);
  });

  it('cliente sem função Artist não acessa o estúdio', async () => {
    signedIn(client, false);
    renderApp('/estudio');
    expect(await screen.findByText('Esta área é exclusiva para artistas.')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Estúdio do artista' })).not.toBeInTheDocument();
  });

  it('artista confirmado pelo backend navega pelo estúdio e identifica a área no navbar', async () => {
    signedIn(artist, true);
    server.use(
      http.get('/api/v1/finance/dashboard', () => problem(503, 'finance.source_unavailable')),
      http.get('/api/v2/engagements', () => HttpResponse.json(page([]))),
      http.get('/api/v1/conversations', () => HttpResponse.json(page([]))),
    );
    renderApp('/estudio');
    expect(await screen.findByRole('heading', { name: 'Seu estúdio, em um só lugar.' })).toBeInTheDocument();
    expect(screen.getByLabelText('Área atual: Estúdio do artista')).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Navegação do estúdio' })).toBeInTheDocument();
  });

  it('sessão expirada durante o uso abre o aviso sem perder a página', async () => {
    signedIn(client, false);
    server.use(
      http.get('/api/v2/engagements', () => problem(401, 'session.required')),
      http.get('/api/v1/scheduling/calendar', () => HttpResponse.json(page([]))),
      http.get('/api/v1/favorites', () => HttpResponse.json(page([]))),
    );
    renderApp('/meu-espaco');
    expect(await screen.findByRole('heading', { name: 'Sua sessão expirou' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /Oi, Júlia/ })).toBeInTheDocument();
  });

  it('sair encerra a sessão no BFF com token antiforgery', async () => {
    signedIn(client, false);
    let csrf: string | null = null;
    server.use(
      http.get('/api/v1/artists', () => HttpResponse.json(page([]))),
      http.post('/bff/logout', ({ request }) => {
        csrf = request.headers.get('X-CSRF-TOKEN');
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderApp('/');
    await userEvent.click(await screen.findByRole('button', { name: 'Sair da conta' }));
    await waitFor(() => expect(csrf).toBe('csrf-token'));
    expect(await screen.findByRole('button', { name: 'Entrar' })).toBeInTheDocument();
  });
});
