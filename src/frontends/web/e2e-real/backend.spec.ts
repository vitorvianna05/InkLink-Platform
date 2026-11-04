import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { expect, test, type Page } from '@playwright/test';

const root = fileURLToPath(new URL('../../../../artifacts/integration/', import.meta.url));
const smoke = JSON.parse(readFileSync(`${root}/smoke.json`, 'utf8')) as {
  users: Record<'client' | 'artist' | 'other', string>; engagement: string; newConversation: string; oldConversation: string;
};
const password = readFileSync(`${root}/.env`, 'utf8').split(/\r?\n/).find((line) => line.startsWith('INTEGRATION_USER_PASSWORD='))?.split('=')[1];
if (!password) throw new Error('Run the P09 synthetic smoke before the browser integration checks.');

/**
 * Chamada de API feita pelo próprio navegador, na origem do website e com os cookies da sessão.
 * O cliente Node do Playwright não aceita o certificado de desenvolvimento do ASP.NET Core
 * (autoassinado, CA:FALSE) como raiz confiável; o navegador o aceita pela confiança do sistema.
 * TLS continua verificado: nenhuma checagem de certificado é desativada.
 */
async function api(page: Page, method: string, path: string, options: { csrf?: string; body?: unknown } = {}) {
  return page.evaluate(async ({ method, path, csrf, body }) => {
    const headers: Record<string, string> = { Accept: 'application/json' };
    if (csrf) headers['X-CSRF-TOKEN'] = csrf;
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(path, { method, headers, credentials: 'same-origin', body: body === undefined ? undefined : JSON.stringify(body) });
    const text = await response.text();
    return { status: response.status, json: (text ? JSON.parse(text) : null) as unknown };
  }, { method, path, csrf: options.csrf, body: options.body });
}

async function antiforgery(page: Page) {
  return ((await api(page, 'GET', '/bff/antiforgery')).json as { requestToken: string }).requestToken;
}

async function login(page: Page, actor: keyof typeof smoke.users) {
  // Decisão 0017 (revisada): o convite leva a /entrar com o destino; o BFF conclui o OIDC e devolve
  // o navegador ao website, no destino, já autenticado.
  await page.goto('/meu-espaco');
  await page.getByRole('main').getByRole('link', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/entrar\?destino=%2Fmeu-espaco$/);
  await expect(page.locator('input')).toHaveCount(0);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await page.locator('#username').fill(smoke.users[actor]);
  await page.locator('#password').fill(password!);
  await page.locator('#kc-login').click();
  await expect(page).toHaveURL('https://localhost:5173/meu-espaco', { timeout: 60_000 });
  await page.getByRole('button', { name: 'Abrir menu da conta' }).click();
  await expect(page.getByRole('button', { name: 'Sair da conta' })).toBeVisible();
  await page.keyboard.press('Escape');
}

test('catálogo público e sugestões reais pelo proxy HTTPS', async ({ page }) => {
  await page.goto('/explorar');
  await expect(page.getByText('Cada história tem um traço.')).toBeVisible();
  await page.getByRole('combobox', { name: 'O que você está procurando?' }).fill('blakwork');
  await expect(page.getByRole('option', { name: /Blackwork/ }).first()).toBeVisible();
  await page.getByRole('combobox', { name: 'O que você está procurando?' }).press('Escape');
  await page.getByRole('combobox', { name: 'O que você está procurando?' }).fill('');
  await page.getByRole('button', { name: /Buscar artistas/ }).click();
  await expect(page.locator('.artist-card').first()).toBeVisible();
  expect((await api(page, 'GET', '/bff/session')).status).toBe(401);
});

test('cliente autentica por OIDC, persiste favorito e mensagem e consulta histórico bloqueado', async ({ page }) => {
  await login(page, 'client');
  const response = await api(page, 'GET', `/api/v2/engagements/${smoke.engagement}`);
  expect(response.status).toBe(200);
  const engagement = response.json as { catalog: { artistId: string; name: string } };
  expect((await api(page, 'DELETE', `/api/v1/favorites/${engagement.catalog.artistId}`, { csrf: await antiforgery(page) })).status).toBe(204);
  await page.goto(`/artistas/${engagement.catalog.artistId}`);
  const favorite = page.getByRole('button', { name: `Salvar ${engagement.catalog.name} nos favoritos` });
  const saved = page.waitForResponse((r) => r.url().includes('/api/v1/favorites/') && r.request().method() === 'PUT');
  await favorite.click();
  expect((await saved).status()).toBe(204);
  await expect(page.getByRole('button', { name: `Remover ${engagement.catalog.name} dos favoritos` })).toHaveAttribute('aria-pressed', 'true');
  await page.reload();
  await expect(page.getByRole('button', { name: `Remover ${engagement.catalog.name} dos favoritos` })).toHaveAttribute('aria-pressed', 'true');
  await page.goto(`/mensagens/${smoke.newConversation}`);
  const text = `Integração web ${crypto.randomUUID()}`;
  await page.getByLabel('Mensagem', { exact: true }).fill(text);
  await page.getByRole('button', { name: 'Enviar mensagem' }).click();
  await expect(page.getByRole('log').getByText(text, { exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('log').getByText(text, { exact: true })).toBeVisible();
  await page.goto(`/mensagens/${smoke.oldConversation}`);
  await expect(page.getByLabel('Mensagem', { exact: true })).toBeDisabled();
  await expect(page.getByText('Serviço encerrado', { exact: true }).last()).toBeVisible();
  // Browser JavaScript cannot read the session/CSRF cookies or any access token.
  const cookies = await page.context().cookies();
  expect(cookies.filter((c) => c.name.startsWith('__Host-inklink-')).every((c) => c.httpOnly && c.secure)).toBe(true);
  expect(await page.evaluate(() => document.cookie)).not.toContain('__Host-inklink-session');
  await page.goto(`/artistas/${engagement.catalog.artistId}?aba=flashes`);
  await page.getByRole('button', { name: /Quero este flash/ }).first().click();
  await page.getByLabel('Sua ideia', { exact: true }).fill('Pedido sintético da integração web');
  await page.getByLabel('Região do corpo', { exact: true }).fill('Braço');
  await page.getByLabel('Tamanho aproximado (cm)', { exact: true }).fill('10');
  const created = page.waitForResponse((r) => r.url().endsWith('/api/v2/engagements') && r.request().method() === 'POST');
  await page.getByRole('button', { name: /Enviar minha ideia/ }).click();
  const result = await created;
  expect(result.status()).toBe(201);
  const order = await result.json() as { id: string; catalog: { flash: { id: string } | null } };
  expect(order.catalog.flash?.id).toBeTruthy();
  await expect(page).toHaveURL(new RegExp(`/atendimentos/${order.id}$`));

});

test('artista acessa indicadores, agenda e atendimento reais pela própria perspectiva', async ({ page }) => {
  await login(page, 'artist');
  await page.goto('/estudio');
  await expect(page.getByRole('heading', { name: 'Seu estúdio, em um só lugar.' })).toBeVisible();
  await expect(page.getByText('Lucro estimado', { exact: true })).toBeVisible();
  await expect(page.locator('[role="alert"]')).toHaveCount(0);
  const date = new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 10);
  expect((await api(page, 'PUT', `/api/v1/scheduling/availability/${date}`, { csrf: await antiforgery(page), body: { timeZoneId: 'America/Sao_Paulo', windows: [] } })).status).toBe(200);
  await page.goto(`/estudio/agenda?dia=${date}`);
  await expect(page.getByRole('heading', { name: 'Espaço para novas histórias.' })).toBeVisible();
  await expect(page.getByText('Nenhuma janela de atendimento neste dia.')).toBeVisible();
  await page.getByRole('button', { name: 'Adicionar janela' }).click();
  const defined = page.waitForResponse((r) => r.url().includes(`/availability/${date}`) && r.request().method() === 'PUT');
  await page.getByRole('button', { name: 'Salvar disponibilidade' }).click();
  const saved = await defined;
  expect(saved.status()).toBe(200);
  expect(saved.request().postDataJSON().windows[0]).toEqual({ start: '10:00:00', end: '18:00:00' });
  await expect(page.getByText('Disponibilidade salva.')).toBeVisible();
  await page.goto(`/estudio/mensagens/${smoke.newConversation}`);
  await expect(page.getByRole('log')).toBeVisible();
  await expect(page.getByLabel('Mensagem', { exact: true })).toBeEnabled();
});

test('menu da conta encerra a sessão real pelo BFF', async ({ page }) => {
  await login(page, 'client');
  await page.getByRole('button', { name: 'Abrir menu da conta' }).click();
  await page.getByRole('button', { name: 'Sair da conta' }).click();
  await expect(page.getByRole('banner').getByRole('link', { name: 'Entrar', exact: true })).toBeVisible();
  expect((await api(page, 'GET', '/bff/session')).status).toBe(401);
});

test('terceiro autenticado não lê conversa de outro atendimento', async ({ page }) => {
  await login(page, 'other');
  expect((await api(page, 'GET', `/api/v1/conversations/${smoke.newConversation}`)).status).toBe(404);
  await page.goto(`/mensagens/${smoke.newConversation}`);
  await expect(page.getByRole('alert').getByText('Não encontramos este conteúdo.')).toBeVisible();
  await expect(page.getByRole('log')).toHaveCount(0);
});
