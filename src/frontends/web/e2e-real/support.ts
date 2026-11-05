import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';

// Dados e contas sintéticos da P09, lidos apenas dos artefatos ignorados do ambiente isolado.
const root = fileURLToPath(new URL('../../../../artifacts/integration/', import.meta.url));
export const smoke = JSON.parse(readFileSync(`${root}/smoke.json`, 'utf8')) as {
  users: Record<'client' | 'artist' | 'other', string>; engagement: string; newConversation: string; oldConversation: string;
};
const password = readFileSync(`${root}/.env`, 'utf8').split(/\r?\n/).find((line) => line.startsWith('INTEGRATION_USER_PASSWORD='))?.split('=')[1];
if (!password) throw new Error('Run the P09 synthetic smoke before the browser integration checks.');

/** Imagem sintética versionada (800x800), usada pelos envios de obra e de foto. */
export const syntheticImage = fileURLToPath(new URL('../../../backend/scripts/integration/assets/synthetic-artwork.png', import.meta.url));

/**
 * Chamada de API feita pelo próprio navegador, na origem do website e com os cookies da sessão.
 * O cliente Node do Playwright não aceita o certificado de desenvolvimento do ASP.NET Core
 * (autoassinado, CA:FALSE) como raiz confiável; o navegador o aceita pela confiança do sistema.
 * TLS continua verificado: nenhuma checagem de certificado é desativada.
 */
export async function api(page: Page, method: string, path: string, options: { csrf?: string; body?: unknown } = {}) {
  return page.evaluate(async ({ method, path, csrf, body }) => {
    const headers: Record<string, string> = { Accept: 'application/json' };
    if (csrf) headers['X-CSRF-TOKEN'] = csrf;
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(path, { method, headers, credentials: 'same-origin', body: body === undefined ? undefined : JSON.stringify(body) });
    const text = await response.text();
    return { status: response.status, json: (text ? JSON.parse(text) : null) as unknown };
  }, { method, path, csrf: options.csrf, body: options.body });
}

export async function antiforgery(page: Page) {
  return ((await api(page, 'GET', '/bff/antiforgery')).json as { requestToken: string }).requestToken;
}

/** Preenche as credenciais na página do provedor (tema InkLink do Keycloak). */
export async function submitCredentials(page: Page, actor: keyof typeof smoke.users) {
  await page.locator('#username').fill(smoke.users[actor]);
  await page.locator('#password').fill(password!);
  await page.locator('#kc-login').click();
}

export async function login(page: Page, actor: keyof typeof smoke.users) {
  // Decisão 0017 (revisada): o convite leva a /entrar com o destino; o BFF conclui o OIDC e devolve
  // o navegador ao website, no destino, já autenticado.
  await page.goto('/meu-espaco');
  await page.getByRole('main').getByRole('link', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/entrar\?destino=%2Fmeu-espaco$/);
  await expect(page.locator('input')).toHaveCount(0);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  // T10.21: a tela de credenciais usa o tema InkLink do Keycloak.
  await expect(page.locator('.inklink-login')).toBeVisible();
  await submitCredentials(page, actor);
  await expect(page).toHaveURL('https://localhost:5173/meu-espaco', { timeout: 60_000 });
  await page.getByRole('button', { name: 'Abrir menu da conta' }).click();
  await expect(page.getByRole('button', { name: 'Sair da conta' })).toBeVisible();
  await page.keyboard.press('Escape');
}
