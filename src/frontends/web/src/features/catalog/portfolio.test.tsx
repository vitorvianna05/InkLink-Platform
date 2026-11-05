import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ArtistProfile, Artwork } from '@/shared/api/contracts';
import { artist, page, problem, renderApp, signedIn } from '@/test/render';
import { server } from '@/test/server';

const profile: ArtistProfile = { id: 'a1', name: 'Rafael Moura', studio: 'Moura Tattoo', city: 'São Paulo', biography: 'Traço fino.', styles: ['Blackwork'], startingPrice: 450 };
const uploaded: Artwork = {
  id: 'f1', title: 'Mariposa', imageUrl: 'https://localhost:5173/media/inklink-catalog-media/artworks/a1/x/1600.webp', alternativeText: 'Mariposa lunar',
  style: 'Blackwork', referencePrice: 400, thumbnailUrl: 'https://localhost:5173/media/inklink-catalog-media/artworks/a1/x/480.webp', externalImage: false,
};
const legacy: Artwork = { id: 't1', title: 'Ramo', imageUrl: 'https://img.example.test/t1.jpg', alternativeText: 'Ramo delicado', style: 'Fineline', referencePrice: null, thumbnailUrl: 'https://img.example.test/t1.jpg', externalImage: true };

function portfolio() {
  signedIn(artist, true);
  window.localStorage.setItem(`inklink.catalog-artist.${artist.id}`, 'a1');
  server.use(
    http.get('/api/v2/engagements', () => HttpResponse.json(page([]))),
    http.get('/api/v1/artists/a1', () => HttpResponse.json(profile)),
    http.get('/api/v1/artists/a1/tattoos', () => HttpResponse.json(page([legacy]))),
    http.get('/api/v1/artists/a1/flashes', () => HttpResponse.json(page([uploaded]))),
  );
}

const png = () => new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'mariposa.png', { type: 'image/png' });

beforeEach(() => {
  URL.createObjectURL = vi.fn(() => 'blob:preview');
  URL.revokeObjectURL = vi.fn();
});
afterEach(() => vi.restoreAllMocks());

async function openNewFlash() {
  renderApp('/estudio/portfolio');
  const flashes = (await screen.findByRole('heading', { name: /Flashes/ })).closest('section')!;
  await userEvent.click(within(flashes).getByRole('button', { name: /Adicionar/ }));
  return screen.getByRole('dialog', { name: 'Nova flash' });
}

describe('portfólio com upload de imagens (T10.17)', () => {
  it('usa a variante de card e sinaliza a imagem externa antiga', async () => {
    portfolio();
    renderApp('/estudio/portfolio');
    expect(await screen.findByAltText('Mariposa lunar')).toHaveAttribute('src', uploaded.thumbnailUrl);
    expect(await screen.findByText('Imagem externa: envie um arquivo')).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: /Endereço HTTPS/ })).not.toBeInTheDocument();
  });

  it('envia a nova obra por multipart na API v2 com o arquivo e CSRF', async () => {
    portfolio();
    // O fetch do Node não serializa o FormData do jsdom; o teste inspeciona o FormData montado pela página.
    let received: { csrf: string | undefined; type: string | undefined; form: FormData } | null = null;
    const original = globalThis.fetch.bind(globalThis);
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      if (String(input) === '/api/v2/artists/me/flashes' && init?.method === 'POST') {
        const headers = init.headers as Record<string, string>;
        received = { csrf: headers['X-CSRF-TOKEN'], type: headers['Content-Type'], form: init.body as FormData };
        return Promise.resolve(new Response(JSON.stringify(uploaded), { status: 201, headers: { 'Content-Type': 'application/json' } }));
      }
      return original(input, init);
    });
    const dialog = await openNewFlash();
    await userEvent.type(within(dialog).getByLabelText('Título'), 'Mariposa');
    await userEvent.upload(within(dialog).getByLabelText('Imagem'), png());
    expect(within(dialog).getByAltText('Prévia da nova imagem')).toHaveAttribute('src', 'blob:preview');
    await userEvent.type(within(dialog).getByLabelText('Texto alternativo'), 'Mariposa lunar');
    await userEvent.type(within(dialog).getByLabelText('Estilo'), 'Blackwork');
    await userEvent.type(within(dialog).getByLabelText('Valor de referência (opcional)'), '400,5');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Salvar' }));
    await waitFor(() => expect(received).not.toBeNull());
    expect(received!.csrf).toBe('csrf-token');
    // Sem Content-Type explícito: o navegador define multipart com o boundary.
    expect(received!.type).toBeUndefined();
    expect(received!.form.get('title')).toBe('Mariposa');
    expect(received!.form.get('alternativeText')).toBe('Mariposa lunar');
    expect(received!.form.get('style')).toBe('Blackwork');
    expect(received!.form.get('referencePrice')).toBe('400.50');
    expect((received!.form.get('image') as File).name).toBe('mariposa.png');
    expect(received!.form.has('imageUrl')).toBe(false);
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nova flash' })).not.toBeInTheDocument());
  });

  it('recusa formato e ausência de imagem no navegador e mostra o erro do backend', async () => {
    portfolio();
    const sent = vi.fn();
    server.use(
      http.post('/api/v2/artists/me/flashes', () => {
        sent();
        return problem(400, 'catalog.invalid', { errors: { image: ['The image must have at least 600 px on its shorter side.'] } });
      }),
    );
    const dialog = await openNewFlash();
    await userEvent.type(within(dialog).getByLabelText('Título'), 'Mariposa');
    await userEvent.type(within(dialog).getByLabelText('Texto alternativo'), 'Mariposa lunar');
    await userEvent.type(within(dialog).getByLabelText('Estilo'), 'Blackwork');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Salvar' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Escolha a imagem da obra.');
    await userEvent.upload(within(dialog).getByLabelText('Imagem'), new File(['GIF89a'], 'a.gif', { type: 'image/gif' }), { applyAccept: false });
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Use uma imagem JPEG, PNG ou WebP.');
    expect(sent).not.toHaveBeenCalled();
    await userEvent.upload(within(dialog).getByLabelText('Imagem'), png());
    await userEvent.click(within(dialog).getByRole('button', { name: 'Salvar' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('600 px');
    expect(sent).toHaveBeenCalledTimes(1);
  });
});
