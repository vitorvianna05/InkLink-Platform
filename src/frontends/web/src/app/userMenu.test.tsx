import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { anonymous, artist, client, page, problem, renderApp, signedIn } from '@/test/render';
import { server } from '@/test/server';

const catalogEmpty = () =>
  server.use(
    http.get('/api/v1/artists', () => HttpResponse.json(page([]))),
    http.get('/api/v1/favorites', () => HttpResponse.json(page([]))),
  );

describe('menu da conta (T10.12)', () => {
  it('visitante abre o menu, encontra Entrar e fecha com Escape devolvendo o foco', async () => {
    anonymous();
    catalogEmpty();
    renderApp('/explorar');
    const trigger = await screen.findByRole('button', { name: 'Abrir menu da conta' });
    await waitFor(() => expect(screen.getAllByRole('link', { name: 'Entrar' })).toHaveLength(1));
    await userEvent.click(trigger);
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    const panel = document.getElementById(trigger.getAttribute('aria-controls')!)!;
    expect(panel).toHaveTextContent('Entre para acompanhar pedidos, conversas e favoritos.');
    const signIn = screen.getAllByRole('link', { name: 'Entrar' }).find((element) => panel.contains(element))!;
    expect(signIn).toHaveAttribute('href', '/entrar?destino=%2Fexplorar');
    expect(signIn).toHaveFocus();
    expect(screen.queryByRole('button', { name: 'Sair da conta' })).not.toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    expect(screen.getByRole('button', { name: 'Abrir menu da conta' })).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByRole('button', { name: 'Abrir menu da conta' })).toHaveFocus();
  });

  it('cliente vê nome e função, sem atalho de estúdio, e clique fora fecha', async () => {
    signedIn(client, false);
    catalogEmpty();
    renderApp('/explorar');
    const trigger = await screen.findByRole('button', { name: 'Abrir menu da conta' });
    await waitFor(() => expect(trigger).toHaveTextContent('JV'));
    await userEvent.click(trigger);
    const panel = document.getElementById(trigger.getAttribute('aria-controls')!)!;
    expect(within(panel).getByText('Júlia Vieira')).toBeInTheDocument();
    expect(within(panel).getByText('Cliente')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Estúdio do artista' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Entrar' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('heading', { name: 'O próximo traço começa com a sua busca.' }));
    expect(screen.queryByText('Júlia Vieira')).not.toBeInTheDocument();
  });

  it('artista no estúdio tem atalho para Meu espaço e sai pelo BFF com antiforgery', async () => {
    signedIn(artist, true);
    let csrf: string | null = null;
    server.use(
      http.get('/api/v1/finance/dashboard', () => problem(503, 'finance.source_unavailable')),
      http.get('/api/v2/engagements', () => HttpResponse.json(page([]))),
      http.get('/api/v1/conversations', () => HttpResponse.json(page([]))),
      http.post('/bff/logout', ({ request }) => {
        csrf = request.headers.get('X-CSRF-TOKEN');
        // Como o BFF real: depois da saída, /bff/session volta a responder 401.
        anonymous();
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderApp('/estudio');
    await screen.findByRole('heading', { name: 'Seu estúdio, em um só lugar.' });
    await userEvent.click(screen.getByRole('button', { name: 'Abrir menu da conta' }));
    expect(screen.getByText('Artista')).toBeInTheDocument();
    expect(screen.getByText('Rafael Moura', { selector: 'strong' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Meu espaço' })).toHaveAttribute('href', '/meu-espaco');
    await userEvent.click(screen.getByRole('button', { name: 'Sair da conta' }));
    await waitFor(() => expect(csrf).toBe('csrf-token'));
    expect((await screen.findAllByRole('link', { name: 'Entrar' }))[0]).toHaveAttribute('href', '/entrar?destino=%2Festudio');
    expect(screen.queryByText('Rafael Moura', { selector: 'strong' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Abrir menu da conta' })).toHaveAttribute('aria-expanded', 'false');
  });
});
