import { expect, test } from '@playwright/test';
import { api } from './support';

// Caixa de entrada do Mailpit isolado da P09 (decisão 0020): e-mail e SMS simulados do cadastro.
const mailpit = 'http://127.0.0.1:5284/api/v1';

/** Código de 8 dígitos da mensagem mais recente enviada ao destino. */
async function codeFor(address: string, after: number): Promise<string> {
  let code = '';
  await expect.poll(async () => {
    const search = (await (await fetch(`${mailpit}/search?query=${encodeURIComponent(`to:"${address}"`)}`)).json()) as { messages: { ID: string; Created: string }[] };
    const latest = search.messages.filter((message) => Date.parse(message.Created) >= after).sort((a, b) => Date.parse(b.Created) - Date.parse(a.Created))[0];
    if (!latest) return '';
    const message = (await (await fetch(`${mailpit}/message/${latest.ID}`)).json()) as { Text: string };
    code = /\b(\d{8})\b/.exec(message.Text)?.[1] ?? '';
    return code;
  }, { timeout: 30_000, intervals: [1_000] }).toMatch(/^\d{8}$/);
  return code;
}

test('visitante cria a conta no Keycloak, confirma e-mail e celular por código e solicita a função de artista', async ({ page }) => {
  test.setTimeout(3 * 60_000);
  const suffix = crypto.randomUUID().slice(0, 8);
  const email = `cadastro-${suffix}@example.test`;
  const phoneDigits = `119${String(Math.floor(Math.random() * 1e8)).padStart(8, '0')}`;
  const password = `InkLink!${crypto.randomUUID()}`;

  // Cadastro na página do provedor (prompt=create), com o tema InkLink; nenhuma senha passa pelo website.
  await page.goto('/entrar?destino=%2Fmeu-espaco');
  await page.getByRole('button', { name: 'Criar conta' }).click();
  await expect(page.locator('.inklink-login')).toBeVisible();
  const form = page.locator('#kc-register-form');
  await form.locator('#firstName').fill('Cadastro');
  await form.locator('#lastName').fill(`Teste ${suffix}`);
  await form.locator('#email').fill(email);
  await form.locator('#password').fill(password);
  await form.locator('#password-confirm').fill(password);
  await form.locator('[type=submit]').click();

  // De volta ao website, pendente: as APIs privadas recusam a conta até a confirmação.
  await expect(page).toHaveURL(/\/cadastro\/confirmar\?destino=%2Fmeu-espaco$/, { timeout: 60_000 });
  await expect(page.getByRole('heading', { level: 1, name: 'Confirme seu cadastro.' })).toBeVisible();
  const blocked = await api(page, 'GET', '/api/v1/conversations');
  expect(blocked.status).toBe(403);

  await page.getByRole('checkbox', { name: /Termos de uso/ }).check();
  await page.getByRole('button', { name: 'Aceitar termos' }).click();
  await expect(page.getByText(/aceita\.$/)).toBeVisible();

  const emailStep = page.getByRole('region', { name: /E-mail/ });
  const emailSent = Date.now() - 2_000;
  await emailStep.getByRole('button', { name: 'Enviar código' }).click();
  await emailStep.getByLabel('Código do e-mail').fill(await codeFor(email, emailSent));
  await emailStep.getByRole('button', { name: 'Confirmar' }).click();
  await expect(page.getByRole('heading', { name: /E-mail \(concluído\)/ })).toBeVisible();

  const phoneStep = page.getByRole('region', { name: /Celular/ });
  await phoneStep.getByLabel('Celular com DDD').fill(phoneDigits);
  await phoneStep.getByRole('button', { name: 'Salvar celular' }).click();
  const phoneSent = Date.now() - 2_000;
  await phoneStep.getByRole('button', { name: 'Enviar código' }).click();
  await phoneStep.getByLabel('Código do celular').fill(await codeFor(`55${phoneDigits}@sms.inklink.test`, phoneSent));
  await phoneStep.getByRole('button', { name: 'Confirmar' }).click();

  await expect(page.getByText('Cadastro concluído. Sua conta está pronta para usar.')).toBeVisible();
  const next = page.getByRole('button', { name: 'Continuar' });
  await expect(next).toBeEnabled({ timeout: 30_000 });
  await next.click();
  await expect(page.getByRole('heading', { name: /Oi, Cadastro/ })).toBeVisible();
  expect((await api(page, 'GET', '/api/v1/conversations')).status).toBe(200);

  // Artist só depois da aprovação administrativa.
  await page.getByRole('button', { name: /Quero ser artista/ }).click();
  await expect(page.getByRole('heading', { name: 'Solicitação em análise' })).toBeVisible();
});
