import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ArtistProfile, Artwork, Engagement } from '@/shared/api/contracts';
import { anonymous, client, page, problem, renderApp, signedIn } from '@/test/render';
import { server } from '@/test/server';

const profile: ArtistProfile = {
  id: 'a1',
  name: 'Camila Azevedo',
  studio: 'Ateliê Linha Fina',
  city: 'São Paulo',
  biography: 'A beleza mora nos detalhes.',
  styles: ['Fineline'],
  startingPrice: 350,
};
const tattoo: Artwork = { id: 't1', title: 'Ramo delicado', imageUrl: 'https://img/t1.jpg', alternativeText: 'Ramo em fineline no pulso', style: 'Fineline', referencePrice: null };
const flash: Artwork = { id: 'f1', title: 'Mariposa lunar', imageUrl: 'https://img/f1.jpg', alternativeText: 'Mariposa com lua', style: 'Fineline', referencePrice: 400 };

function catalog() {
  server.use(
    http.get('/api/v1/artists/a1', () => HttpResponse.json(profile)),
    http.get('/api/v1/artists/a1/tattoos', () => HttpResponse.json(page([tattoo]))),
    http.get('/api/v1/artists/a1/flashes', () => HttpResponse.json(page([flash]))),
  );
}

describe('perfil público', () => {
  it('mostra o artista escolhido com abas distintas e navegáveis por teclado', async () => {
    anonymous();
    catalog();
    renderApp('/artistas/a1');
    expect(await screen.findByRole('heading', { name: 'Camila Azevedo' })).toBeInTheDocument();
    expect(await screen.findByAltText('Ramo em fineline no pulso')).toBeInTheDocument();
    const tab = screen.getByRole('tab', { name: 'Tatuagens realizadas' });
    expect(tab).toHaveAttribute('aria-selected', 'true');
    tab.focus();
    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'Flashes' })).toHaveAttribute('aria-selected', 'true');
    expect(await screen.findByAltText('Mariposa com lua')).toBeInTheDocument();
    expect(screen.queryByAltText('Ramo em fineline no pulso')).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /Conversa/ })).not.toBeInTheDocument();
  });

  it('artista inexistente informa o erro e oferece navegação', async () => {
    anonymous();
    server.use(http.get('/api/v1/artists/zz', () => problem(404, 'catalog.not_found')));
    renderApp('/artistas/zz');
    expect(await screen.findByText('Não encontramos este conteúdo.')).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'Explorar artistas' }).length).toBeGreaterThan(0);
  });

  it('pedido a partir de flash valida campos e envia com Idempotency-Key', async () => {
    signedIn(client);
    catalog();
    let body: Record<string, unknown> | null = null;
    let key: string | null = null;
    const created: Engagement = {
      id: 'e1',
      clientId: client.id,
      artistAccountId: 'acc',
      idea: 'Mariposa no ombro',
      bodyRegion: 'Ombro',
      approximateSizeCm: 10,
      catalog: { artistId: 'a1', name: profile.name, studio: profile.studio, city: profile.city, startingPrice: 350, flash: { ...flash } },
      state: 'Negotiating',
      createdAt: new Date().toISOString(),
      revision: 1,
      currentProposalId: null,
      currentProposal: null,
    };
    server.use(
      http.get('/api/v2/engagements', () => HttpResponse.json(page([]))),
      http.get('/api/v1/conversations', () => HttpResponse.json(page([]))),
      http.get('/api/v1/favorites', () => HttpResponse.json(page([]))),
      http.post('/api/v2/engagements', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        key = request.headers.get('Idempotency-Key');
        return HttpResponse.json(created, { status: 201 });
      }),
      http.get('/api/v2/engagements/e1', () => HttpResponse.json(created)),
      http.get('/api/v2/engagements/e1/proposals', () => HttpResponse.json(page([]))),
      http.get('/api/v2/engagements/e1/lifecycle', () => HttpResponse.json({ requiresReservation: false, state: 'Negotiating', operations: [] })),
      http.get('/api/v1/scheduling/calendar', () => HttpResponse.json(page([]))),
    );
    renderApp('/artistas/a1?aba=flashes');
    await userEvent.click(await screen.findByRole('button', { name: /Quero este flash/ }));
    expect(screen.getByText(/A partir do flash/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /Enviar minha ideia/ }));
    expect(screen.getByText('Descreva sua ideia.')).toBeInTheDocument();
    expect(screen.getByText('Informe a região do corpo.')).toBeInTheDocument();
    expect(body).toBeNull();

    await userEvent.type(screen.getByLabelText('Sua ideia'), 'Mariposa no ombro');
    await userEvent.type(screen.getByLabelText('Região do corpo'), 'Ombro');
    await userEvent.type(screen.getByLabelText('Tamanho aproximado (cm)'), '10');
    await userEvent.click(screen.getByRole('button', { name: /Enviar minha ideia/ }));

    expect(await screen.findByText(/Sua ideia foi enviada a Camila Azevedo/)).toBeInTheDocument();
    expect(body).toEqual({ artistId: 'a1', idea: 'Mariposa no ombro', bodyRegion: 'Ombro', approximateSizeCm: 10, flashId: 'f1' });
    expect(key).toMatch(/^[0-9a-f-]{36}$/);
  });
  it('aguarda a sessão antes de habilitar favoritos e pedidos', async () => {
    signedIn(client);
    catalog();
    let complete!: () => void;
    const pending = new Promise<void>((resolve) => { complete = resolve; });
    server.use(
      http.get('/api/v1/favorites', () => HttpResponse.json(page([]))),
      http.get('/api/v2/engagements', () => HttpResponse.json(page([]))),
      http.get('/api/v1/conversations', () => HttpResponse.json(page([]))),
    );
    server.use(http.get('/bff/session', async () => {
      await pending;
      return HttpResponse.json({ authenticated: true, name: client.displayName });
    }));
    renderApp('/artistas/a1?aba=flashes');
    try {
      expect(await screen.findByRole('button', { name: 'Contar minha ideia' })).toBeDisabled();
      expect(screen.getByRole('button', { name: /Salvar .* nos favoritos/ })).toBeDisabled();
      expect(await screen.findByRole('button', { name: /Quero este flash/ })).toBeDisabled();
    } finally { complete(); }
    await userEvent.click(screen.getByRole('button', { name: 'Abrir menu da conta' }));
    expect(await screen.findByRole('button', { name: 'Sair da conta' })).toBeInTheDocument();
  });

});
