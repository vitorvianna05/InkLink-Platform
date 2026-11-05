import { expect, test, type Page } from '@playwright/test';
import { mockBackend } from './mock-backend';

const widths = [360, 768, 1280];

const clientPages = ['/', '/explorar?buscar=1', '/artistas/a1', '/meu-espaco', '/mensagens', '/mensagens/c1', '/atendimentos/e1'];
const artistPages = ['/estudio', '/estudio/agenda?dia=2030-01-15', '/estudio/mensagens', '/estudio/financeiro', '/estudio/portfolio', '/estudio/atendimentos/e1'];

async function expectNoHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
}

for (const width of widths) {
  test.describe(`largura ${width}px`, () => {
    test.use({ viewport: { width, height: 900 } });

    for (const path of clientPages) {
      test(`cliente ${path} sem sobreposição horizontal`, async ({ page }) => {
        await mockBackend(page, 'client');
        await page.goto(path);
        await expect(page.locator('main h1, main h2').first()).toBeVisible();
        await page.waitForLoadState('networkidle');
        await expect(page.getByText(/^Carregando/)).toHaveCount(0);
        await expectNoHorizontalOverflow(page);
        await page.screenshot({ path: `test-results/screens/${width}${path.replace(/[/?=]/g, '_') || '_home'}.png`, fullPage: true });
      });
    }

    for (const path of artistPages) {
      test(`artista ${path} sem sobreposição horizontal`, async ({ page }) => {
        await mockBackend(page, 'artist');
        await page.goto(path);
        await expect(page.locator('main h1').first()).toBeVisible();
        await page.waitForLoadState('networkidle');
        await expect(page.getByText(/^Carregando/)).toHaveCount(0);
        await expectNoHorizontalOverflow(page);
        await page.screenshot({ path: `test-results/screens/${width}${path.replace(/[/?=]/g, '_')}.png`, fullPage: true });
      });
    }
  });
}

test('recarregar o endereço de uma conversa mantém a seleção', async ({ page }) => {
  await mockBackend(page, 'client');
  await page.goto('/mensagens/c1');
  await expect(page.getByRole('heading', { name: 'Rafael Moura' })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Rafael Moura' })).toBeVisible();
  await expect(page.getByText('Oi, Rafael! Quero uma tattoo botânica')).toBeVisible();
});

test('sugestões funcionam só com teclado', async ({ page }) => {
  await mockBackend(page, 'anonymous');
  await page.goto('/explorar');
  const input = page.getByRole('combobox', { name: 'O que você está procurando?' });
  await input.focus();
  await page.keyboard.type('sa');
  await expect(page.getByRole('listbox').getByRole('option', { name: /São Paulo/ })).toBeVisible();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(input).toHaveValue('São Paulo');
  await page.keyboard.type(' ');
  await page.keyboard.press('Escape');
  await expect(page.getByRole('listbox')).toBeHidden();
});

test('diálogo recebe o foco, mantém o contexto e devolve o foco ao fechar', async ({ page }) => {
  await mockBackend(page, 'client');
  await page.goto('/atendimentos/e1');
  const open = page.getByRole('button', { name: 'Aceitar proposta' });
  await open.focus();
  await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog', { name: 'Confirmar o aceite?' });
  await expect(dialog).toBeVisible();
  const focusedInside = await dialog.evaluate((node) => node.contains(document.activeElement));
  expect(focusedInside).toBe(true);
  for (let i = 0; i < 4; i++) await page.keyboard.press('Tab');
  expect(await dialog.evaluate((node) => node.contains(document.activeElement) || document.activeElement === document.body)).toBe(true);
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(open).toBeFocused();
});

test('abas do perfil respondem às setas', async ({ page }) => {
  await mockBackend(page, 'anonymous');
  await page.goto('/artistas/a1');
  const first = page.getByRole('tab', { name: 'Tatuagens realizadas' });
  await first.focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('tab', { name: 'Flashes' })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('tab', { name: 'Flashes' })).toBeFocused();
});

test('diálogo da foto de perfil recebe o foco e o devolve ao menu da conta', async ({ page }) => {
  await mockBackend(page, 'client');
  await page.goto('/meu-espaco');
  const trigger = page.getByRole('button', { name: 'Abrir menu da conta' });
  await trigger.click();
  await page.getByRole('button', { name: 'Foto de perfil' }).click();
  const dialog = page.getByRole('dialog', { name: 'Foto de perfil' });
  await expect(dialog).toBeVisible();
  expect(await dialog.evaluate((node) => node.contains(document.activeElement))).toBe(true);
  await expect(dialog.getByLabel('Escolher imagem')).toBeAttached();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(page.getByRole('button', { name: 'Abrir menu da conta' })).toBeFocused();
});

test('envio de obra no Portfólio é operável por teclado e sinaliza obra legada', async ({ page }) => {
  await mockBackend(page, 'artist');
  await page.goto('/estudio/portfolio');
  const flashes = page.locator('section').filter({ has: page.getByRole('heading', { name: /^Flashes/ }) });
  await expect(flashes.getByText('Imagem externa: envie um arquivo')).toBeVisible();
  const add = flashes.getByRole('button', { name: /Adicionar/ });
  await add.focus();
  await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog', { name: 'Nova flash' });
  await expect(dialog).toBeVisible();
  expect(await dialog.evaluate((node) => node.contains(document.activeElement))).toBe(true);
  await expect(dialog.getByLabel('Imagem')).toBeAttached();
  await expect(dialog.getByLabel('Texto alternativo')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(add).toBeFocused();
});

for (const width of widths) {
  test(`página de login ${width}px sem sobreposição e com layout dividido`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await mockBackend(page, 'anonymous');
    await page.goto('/entrar?destino=%2Fmeu-espaco');
    await expect(page.getByRole('heading', { level: 1, name: 'Bem-vindo ao InkLink.' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Entrar', exact: true })).toBeEnabled();
    await expect(page.locator('input')).toHaveCount(0);
    await expectNoHorizontalOverflow(page);
    const brand = await page.locator('.login-brand').boundingBox();
    const panel = await page.locator('.login-panel').boundingBox();
    if (width >= 1280) {
      expect(Math.abs(brand!.width - panel!.width)).toBeLessThanOrEqual(1);
      expect(Math.abs(brand!.y - panel!.y)).toBeLessThanOrEqual(1);
    } else {
      expect(panel!.y).toBeGreaterThanOrEqual(brand!.y + brand!.height - 1);
    }
    await page.screenshot({ path: `test-results/screens/${width}_entrar.png`, fullPage: true });
  });

  test(`menu da conta ${width}px abre informações do usuário`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await mockBackend(page, 'client');
    await page.goto('/meu-espaco');
    const trigger = page.getByRole('button', { name: 'Abrir menu da conta' });
    // Foto de perfil (T10.18) no lugar das iniciais.
    await expect(trigger.locator('img')).toHaveAttribute('src', /^data:image/);
    await trigger.focus();
    await page.keyboard.press('Enter');
    const panel = page.locator('.user-menu-panel');
    await expect(panel.getByText('Júlia Vieira')).toBeVisible();
    await expect(panel.getByRole('button', { name: 'Sair da conta' })).toBeVisible();
    // Até 860 px o painel também reúne a navegação da área.
    await expect(panel.getByRole('link', { name: 'Explorar' })).toBeVisible({ visible: width <= 860 });
    expect(await panel.evaluate((node) => node.contains(document.activeElement))).toBe(true);
    const box = await panel.boundingBox();
    expect(box!.x).toBeGreaterThanOrEqual(0);
    expect(box!.x + box!.width).toBeLessThanOrEqual(width);
    await expectNoHorizontalOverflow(page);
    await page.screenshot({ path: `test-results/screens/${width}_menu-conta.png` });
    await page.keyboard.press('Escape');
    await expect(panel).toBeHidden();
    await expect(page.getByRole('button', { name: 'Abrir menu da conta' })).toBeFocused();
  });
}
