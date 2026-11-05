import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Profile } from '@/shared/api/contracts';
import { client, page, renderApp, signedIn } from '@/test/render';
import { server } from '@/test/server';

const photo = 'https://localhost:5173/media/inklink-identity-private/profiles/c/1/128.webp?X-Amz-Signature=abc';

beforeEach(() => {
  URL.createObjectURL = vi.fn(() => 'blob:photo');
  URL.revokeObjectURL = vi.fn();
  server.use(http.get('/api/v1/artists', () => HttpResponse.json(page([]))), http.get('/api/v1/favorites', () => HttpResponse.json(page([]))));
});
afterEach(() => vi.restoreAllMocks());

async function openPhotoDialog() {
  renderApp('/explorar');
  const trigger = await screen.findByRole('button', { name: 'Abrir menu da conta' });
  await waitFor(() => expect(trigger.querySelector('img, .avatar')).not.toBeNull());
  await userEvent.click(trigger);
  await userEvent.click(screen.getByRole('button', { name: 'Foto de perfil' }));
  return screen.getByRole('dialog', { name: 'Foto de perfil' });
}

describe('foto de perfil (T10.18)', () => {
  it('mostra a foto no menu e volta às iniciais se a imagem falhar', async () => {
    signedIn({ ...client, photoUrl: photo.replace('128', '512'), photoThumbnailUrl: photo } satisfies Profile);
    renderApp('/explorar');
    const trigger = await screen.findByRole('button', { name: 'Abrir menu da conta' });
    await waitFor(() => expect(trigger.querySelector('img')).toHaveAttribute('src', photo));
    fireEvent.error(trigger.querySelector('img')!);
    expect(trigger).toHaveTextContent('JV');
  });

  it('envia a foto por multipart e atualiza a sessão', async () => {
    let profile: Profile = { ...client };
    signedIn(client);
    server.use(http.get('/api/v1/profiles/me', () => HttpResponse.json(profile)));
    let form: FormData | null = null;
    const original = globalThis.fetch.bind(globalThis);
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      if (String(input) === '/api/v1/profiles/me/photo' && init?.method === 'PUT') {
        form = init.body as FormData;
        profile = { ...client, photoUrl: photo.replace('128', '512'), photoThumbnailUrl: photo };
        return Promise.resolve(new Response(JSON.stringify({ photoUrl: profile.photoUrl, photoThumbnailUrl: photo }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
      }
      return original(input, init);
    });
    const dialog = await openPhotoDialog();
    expect(within(dialog).getByText(/visível apenas para os artistas com quem você tem atendimento/)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Salvar foto' })).toBeDisabled();
    await userEvent.upload(within(dialog).getByLabelText('Escolher imagem'), new File([new Uint8Array([0xff, 0xd8, 0xff])], 'eu.jpg', { type: 'image/jpeg' }));
    expect(within(dialog).getByAltText('Prévia da nova foto')).toHaveAttribute('src', 'blob:photo');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Salvar foto' }));
    await waitFor(() => expect(form).not.toBeNull());
    expect((form!.get('image') as File).name).toBe('eu.jpg');
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Foto de perfil' })).not.toBeInTheDocument());
    await waitFor(() => expect(screen.getByRole('button', { name: 'Abrir menu da conta' }).querySelector('img')).toHaveAttribute('src', photo));
  });

  it('recusa formato inválido no navegador e remove a foto atual', async () => {
    let profile: Profile = { ...client, photoUrl: photo, photoThumbnailUrl: photo };
    signedIn(client);
    let removed = false;
    server.use(
      http.get('/api/v1/profiles/me', () => HttpResponse.json(profile)),
      http.delete('/api/v1/profiles/me/photo', ({ request }) => {
        removed = request.headers.get('X-CSRF-TOKEN') === 'csrf-token';
        profile = { ...client };
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const dialog = await openPhotoDialog();
    await userEvent.upload(within(dialog).getByLabelText('Escolher imagem'), new File(['GIF89a'], 'a.gif', { type: 'image/gif' }), { applyAccept: false });
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Use uma imagem JPEG, PNG ou WebP.');
    expect(within(dialog).getByRole('button', { name: 'Salvar foto' })).toBeDisabled();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Remover foto' }));
    await waitFor(() => expect(removed).toBe(true));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Abrir menu da conta' })).toHaveTextContent('JV'));
  });
});
