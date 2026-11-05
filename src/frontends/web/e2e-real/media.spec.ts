import { expect, test, type Browser, type Locator, type Page } from '@playwright/test';
import { api, login, smoke, syntheticImage } from './support';

const origin = 'https://localhost:5173';

async function signedPage(browser: Browser, actor: 'client' | 'artist') {
  const context = await browser.newContext({ baseURL: origin });
  const page = await context.newPage();
  await login(page, actor);
  return page;
}

/** A imagem foi baixada pelo proxy /media e decodificada pelo navegador. */
async function expectLoaded(image: Locator) {
  // Avalia com prazo curto: se o Avatar voltar às iniciais, o <img> some e a falha mostra o último estado.
  const state = () => image.evaluate((node: HTMLImageElement) => ({ loaded: node.complete && node.naturalWidth > 0, src: node.currentSrc.split('?')[0] }), undefined, { timeout: 2_000 })
    .catch(() => ({ loaded: false, src: 'sem <img> (iniciais)' }));
  await expect.poll(state, { timeout: 30_000, message: 'imagem carregada pelo proxy /media' }).toMatchObject({ loaded: true });
}

async function uploadPhoto(page: Page) {
  await page.getByRole('button', { name: 'Abrir menu da conta' }).click();
  await page.getByRole('button', { name: 'Foto de perfil' }).click();
  const dialog = page.getByRole('dialog', { name: 'Foto de perfil' });
  await dialog.getByLabel('Escolher imagem').setInputFiles(syntheticImage);
  await dialog.getByRole('button', { name: 'Salvar foto' }).click();
  await expect(dialog).toBeHidden({ timeout: 30_000 });
}

test('artista envia obra por upload no Portfólio e a imagem pública é servida em /media', async ({ browser }) => {
  const artist = await signedPage(browser, 'artist');
  await artist.goto('/estudio/portfolio');
  const flashes = artist.locator('section').filter({ has: artist.getByRole('heading', { name: /^Flashes/ }) });
  await flashes.getByRole('button', { name: /Adicionar/ }).click();
  const dialog = artist.getByRole('dialog', { name: 'Nova flash' });
  const title = `Flash enviado ${crypto.randomUUID().slice(0, 8)}`;
  await dialog.getByLabel('Título').fill(title);
  await dialog.getByLabel('Imagem').setInputFiles(syntheticImage);
  await expect(dialog.getByAltText('Prévia da nova imagem')).toBeVisible();
  await dialog.getByLabel('Texto alternativo').fill(`Ilustração ${title}`);
  await dialog.getByLabel('Estilo').fill('Blackwork');
  await dialog.getByLabel('Valor de referência (opcional)').fill('380');
  await dialog.getByRole('button', { name: 'Salvar' }).click();
  await expect(dialog).toBeHidden({ timeout: 30_000 });
  const image = flashes.getByAltText(`Ilustração ${title}`);
  await expect(image).toHaveAttribute('src', /\/media\/inklink-catalog-media\/artworks\/.+\/480\.webp$/);
  await expectLoaded(image);
  await artist.context().close();
});

test('foto do cliente é privada e visível ao artista em contato; a do artista chega ao perfil público', async ({ browser }) => {
  const client = await signedPage(browser, 'client');
  await uploadPhoto(client);
  const own = client.getByRole('button', { name: 'Abrir menu da conta' }).locator('img');
  await expect(own).toHaveAttribute('src', /\/media\/inklink-identity-private\/profiles\/.+X-Amz-Signature=/);
  await expectLoaded(own);
  // Sem assinatura, o objeto privado é recusado pelo armazenamento.
  const unsigned = (await own.getAttribute('src'))!.split('?')[0];
  expect(await client.evaluate(async (url) => (await fetch(url, { credentials: 'omit' })).status, unsigned)).toBe(403);

  const artist = await signedPage(browser, 'artist');
  await artist.goto(`/estudio/mensagens/${smoke.newConversation}`);
  const counterpart = artist.locator('main .avatar img').first();
  await expect(counterpart).toHaveAttribute('src', /inklink-identity-private\/profiles\/.+X-Amz-Signature=/);
  await expectLoaded(counterpart);

  await uploadPhoto(artist);
  const engagement = (await api(client, 'GET', `/api/v2/engagements/${smoke.engagement}`)).json as { catalog: { artistId: string } };
  // A projeção do Catálogo recebe a foto pública pelo evento da Identity (Outbox e RabbitMQ).
  await expect.poll(async () => {
    const profile = (await api(client, 'GET', `/api/v1/artists/${engagement.catalog.artistId}`)).json as { photoUrl?: string | null };
    return profile.photoUrl ?? '';
  }, { timeout: 60_000, intervals: [2_000] }).toMatch(/\/media\/inklink-identity-media\/profiles\/.+\/512\.webp$/);
  await client.goto(`/artistas/${engagement.catalog.artistId}`);
  const publicPhoto = client.locator('.profile-header .avatar img');
  await expectLoaded(publicPhoto);

  await client.context().close();
  await artist.context().close();
});
