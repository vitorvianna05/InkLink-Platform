import { act, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { anonymous, client, page, renderApp, signedIn } from '@/test/render';
import { browser } from '@/shared/lib/browser';
import { server } from '@/test/server';

const catalogEmpty = () =>
  server.use(
    http.get('/api/v1/artists', () => HttpResponse.json(page([]))),
    http.get('/api/v1/favorites', () => HttpResponse.json(page([]))),
  );

afterEach(() => vi.restoreAllMocks());

describe('página de login (T10.14)', () => {
  it('apresenta marca e ação de entrada sem campos de credenciais', async () => {
    anonymous();
    const { container } = renderApp('/entrar');
    expect(await screen.findByRole('button', { name: 'Entrar' })).toBeEnabled();
    expect(screen.getByRole('heading', { level: 1, name: 'Bem-vindo ao InkLink.' })).toBeInTheDocument();
    expect(screen.getByRole('complementary', { name: 'Sobre o InkLink' })).toHaveTextContent('Marcando a pele.');
    expect(container.querySelectorAll('input, textarea')).toHaveLength(0);
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Voltar para Descobrir' })).toHaveAttribute('href', '/');
  });

  it('redireciona ao login do BFF com o destino validado como returnUrl', async () => {
    anonymous();
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => undefined);
    renderApp('/entrar?destino=%2Festudio%2Fagenda%3Fdia%3D2030-01-15');
    await userEvent.click(await screen.findByRole('button', { name: 'Entrar' }));
    expect(assign).toHaveBeenCalledWith('/bff/login?returnUrl=%2Festudio%2Fagenda%3Fdia%3D2030-01-15');
    expect(screen.getByRole('button', { name: 'Redirecionando…' })).toBeDisabled();
    expect(screen.getByRole('status')).toHaveTextContent('Abrindo a página segura de login do InkLink.');
  });

  it('destino externo vira Descobrir no returnUrl', async () => {
    anonymous();
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => undefined);
    renderApp('/entrar?destino=https%3A%2F%2Fevil.example%2F');
    await userEvent.click(await screen.findByRole('button', { name: 'Entrar' }));
    expect(assign).toHaveBeenCalledWith('/bff/login?returnUrl=%2F');
  });

  it('informa falha devolvida pelo provedor sem simular sessão', async () => {
    anonymous();
    renderApp('/entrar?erro=login&destino=%2Fmeu-espaco');
    expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível concluir o login.');
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeEnabled();
  });

  it('volta à página restaurada do histórico com o botão liberado', async () => {
    anonymous();
    vi.spyOn(browser, 'assign').mockImplementation(() => undefined);
    renderApp('/entrar');
    await userEvent.click(await screen.findByRole('button', { name: 'Entrar' }));
    expect(screen.getByRole('button', { name: 'Redirecionando…' })).toBeDisabled();
    act(() => {
      window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
    });
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeEnabled();
  });

  it('usuário autenticado é redirecionado e destino externo volta a Descobrir', async () => {
    signedIn(client);
    catalogEmpty();
    renderApp('/entrar?destino=https%3A%2F%2Fevil.example%2F');
    expect(await screen.findByRole('heading', { name: 'Descubra. Inspire-se. Conecte-se.' })).toBeInTheDocument();
  });

  it('área privada leva à página de login com o destino preservado', async () => {
    anonymous();
    renderApp('/meu-espaco');
    await screen.findByText('Entre para ver seus pedidos, conversas e favoritos.');
    const links = screen.getAllByRole('link', { name: 'Entrar' });
    expect(links.length).toBeGreaterThan(1);
    for (const link of links) expect(link).toHaveAttribute('href', '/entrar?destino=%2Fmeu-espaco');
  });
});
