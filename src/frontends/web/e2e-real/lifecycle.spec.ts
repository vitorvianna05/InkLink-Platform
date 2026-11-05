import { expect, test, type Browser, type Page } from '@playwright/test';
import { antiforgery, api, login, smoke, submitCredentials } from './support';

const origin = 'https://localhost:5173';
const zone = 'America/Sao_Paulo';

async function signedPage(browser: Browser, actor: 'client' | 'artist') {
  const context = await browser.newContext({ baseURL: origin, ignoreHTTPSErrors: false });
  const page = await context.newPage();
  await login(page, actor);
  return page;
}

/** Data e hora locais do artista (fuso IANA da Agenda), com precisão de minutos. */
function local(date: Date) {
  const parts = Object.fromEntries(new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
    .formatToParts(date).map((part) => [part.type, part.value]));
  return { day: `${parts.year}-${parts.month}-${parts.day}`, time: `${parts.hour}:${parts.minute}` };
}

interface AvailabilityWindow {
  start: string;
  end: string;
}

/** Une janelas sobrepostas ou contíguas (horários `HH:mm:ss` do mesmo dia). */
function mergeWindows(windows: AvailabilityWindow[]): AvailabilityWindow[] {
  const sorted = windows.map((w) => ({ start: w.start.slice(0, 8), end: w.end.slice(0, 8) })).sort((a, b) => a.start.localeCompare(b.start));
  const merged: AvailabilityWindow[] = [];
  for (const window of sorted) {
    const last = merged.at(-1);
    if (last && window.start <= last.end) last.end = window.end > last.end ? window.end : last.end;
    else merged.push({ ...window });
  }
  return merged;
}

async function openEngagement(page: Page, path: string) {
  await page.goto(path);
  await expect(page.locator('main h1').first()).toBeVisible();
}

test('cliente e artista completam proposta, reserva, conflito, encerramento recusado, sessão, lançamento e encerramento pelo website', async ({ browser }) => {
  test.setTimeout(10 * 60_000);
  const client = await signedPage(browser, 'client');
  const artist = await signedPage(browser, 'artist');
  const context = (await api(client, 'GET', `/api/v2/engagements/${smoke.engagement}`)).json as { catalog: { artistId: string } };

  // 1. Pedido a partir de um flash, pelo cliente.
  const idea = `Ciclo web ${crypto.randomUUID().slice(0, 8)}`;
  await client.goto(`/artistas/${context.catalog.artistId}?aba=flashes`);
  await client.getByRole('button', { name: /Quero este flash/ }).first().click();
  await client.getByLabel('Sua ideia', { exact: true }).fill(idea);
  await client.getByLabel('Região do corpo', { exact: true }).fill('Antebraço');
  await client.getByLabel('Tamanho aproximado (cm)', { exact: true }).fill('9');
  await client.getByRole('button', { name: /Enviar minha ideia/ }).click();
  await expect(client).toHaveURL(/\/atendimentos\/[0-9a-f-]{36}$/);
  const engagement = client.url().split('/').pop()!;

  // 2. Proposta do artista e aceite explícito do cliente.
  await openEngagement(artist, `/estudio/atendimentos/${engagement}`);
  await artist.getByLabel('Descrição', { exact: true }).fill('Sessão única com desenho exclusivo');
  await artist.getByLabel('Valor (R$)', { exact: true }).fill('450');
  await artist.getByRole('button', { name: 'Enviar proposta' }).click();
  await expect(artist.getByText('Proposta enviada ao cliente.')).toBeVisible();
  await openEngagement(client, `/atendimentos/${engagement}`);
  await client.getByRole('button', { name: 'Aceitar proposta' }).click();
  await client.getByRole('dialog', { name: 'Confirmar o aceite?' }).getByRole('button', { name: 'Confirmar aceite' }).click();
  await expect(client.getByText('Proposta aceita').first()).toBeVisible();

  // 3. Reserva de um minuto no futuro próximo, como na regressão P09, e conflito no mesmo intervalo.
  const startAt = new Date(Math.ceil(Date.now() / 60_000) * 60_000 + 2 * 60_000);
  const endAt = new Date(startAt.getTime() + 60_000);
  const [start, end] = [local(startAt), local(endAt)];
  test.skip(start.day !== end.day, 'Executar fora da virada do dia no fuso do artista.');
  const csrf = await antiforgery(artist);
  // A disponibilidade do dia é substituída por inteiro: mantém as janelas que já cobrem reservas
  // (execuções anteriores) e acrescenta a deste ciclo, unindo as que se sobrepõem.
  const current = (await api(artist, 'GET', `/api/v1/scheduling/availability/${start.day}`)).json as { windows: AvailabilityWindow[] };
  const windows = mergeWindows([...current.windows, { start: `${start.time}:00`, end: `${local(new Date(endAt.getTime() + 5 * 60_000)).time}:00` }]);
  expect((await api(artist, 'PUT', `/api/v1/scheduling/availability/${start.day}`, { csrf, body: { timeZoneId: zone, windows } })).status).toBe(200);
  await artist.goto(`/estudio/agenda?dia=${start.day}&atendimento=${engagement}`);
  const form = artist.locator('form').filter({ has: artist.getByRole('heading', { name: 'Confirmar reserva' }) });
  await form.getByLabel('Atendimento').selectOption(engagement);
  await form.getByLabel('Início').fill(start.time);
  await form.getByLabel('Término').fill(end.time);
  await form.getByRole('button', { name: 'Confirmar reserva' }).click();
  await expect(form.getByText(/Reserva confirmada para/)).toBeVisible();
  await form.getByLabel('Início').fill(start.time);
  await form.getByLabel('Término').fill(end.time);
  await form.getByRole('button', { name: 'Confirmar reserva' }).click();
  await expect(form.getByText(/conflita com outra reserva/)).toBeVisible();

  // 4. Encerramento recusado enquanto há sessão com término futuro.
  await openEngagement(client, `/atendimentos/${engagement}`);
  await client.getByRole('button', { name: 'Encerrar serviço' }).click();
  await client.getByRole('button', { name: 'Confirmar encerramento' }).click();
  await expect(client.getByText(/Há sessões com término futuro/)).toBeVisible({ timeout: 60_000 });

  // 5. Depois do término real, conclusão da sessão e recebimento registrados pelo artista.
  await expect.poll(() => Date.now(), { timeout: 5 * 60_000, intervals: [5_000] }).toBeGreaterThan(endAt.getTime() + 3_000);
  await artist.goto(`/estudio/agenda?dia=${start.day}`);
  await artist.getByRole('button', { name: 'Registrar conclusão da sessão' }).first().click();
  await artist.getByRole('button', { name: 'Concluir' }).click();
  await expect(artist.getByText(/Sessão concluída/).first()).toBeVisible();
  await artist.goto('/estudio/financeiro');
  const entry = artist.locator('form').filter({ has: artist.getByRole('heading', { name: 'Novo lançamento' }) });
  await entry.getByLabel('Valor (R$)').fill('450,00');
  await entry.getByLabel('Descrição').fill(`Recebimento ${idea}`);
  await entry.getByLabel(/^Atendimento/).selectOption(engagement);
  await entry.getByRole('button', { name: 'Registrar' }).click();
  await expect(entry.getByText('Lançamento registrado.')).toBeVisible();

  // 6. Encerramento definitivo: chat bloqueado e histórico preservado.
  await openEngagement(client, `/atendimentos/${engagement}`);
  // A recusa do passo 4 (sessão futura) exige um novo pedido depois do fim da sessão (decisão 0013).
  await expect(client.getByText(/último pedido de encerramento foi recusado/)).toBeVisible();
  await client.getByRole('button', { name: 'Encerrar serviço' }).click();
  // Aguarda o 202 antes de recarregar: recarregar com o POST em andamento o cancelaria (499) sem efeito.
  const [closure] = await Promise.all([
    client.waitForResponse((response) => response.request().method() === 'POST' && response.url().endsWith(`/api/v2/engagements/${engagement}/closure`)),
    client.getByRole('button', { name: 'Confirmar encerramento' }).click(),
  ]);
  expect(closure.status()).toBe(202);
  const closed = client.getByText('Serviço encerrado', { exact: true }).first();
  await expect.poll(async () => {
    await client.reload();
    return closed.waitFor({ timeout: 10_000 }).then(() => true, () => false);
  }, { timeout: 120_000, intervals: [3_000] }).toBe(true);

  await client.context().close();
  await artist.context().close();
});

test('sessão perdida durante o formulário abre o aviso, preserva o texto e reenvia sem duplicar', async ({ page }) => {
  await login(page, 'client');
  await page.goto(`/mensagens/${smoke.newConversation}`);
  const text = `Mensagem após novo login ${crypto.randomUUID()}`;
  const input = page.getByLabel('Mensagem', { exact: true });
  await input.fill(text);
  // O ticket deixa de valer no navegador: a próxima escrita recebe 401 do BFF, como numa expiração.
  await page.context().clearCookies({ name: '__Host-inklink-session' });
  await page.getByRole('button', { name: 'Enviar mensagem' }).click();
  const dialog = page.getByRole('dialog', { name: 'Sua sessão expirou' });
  await expect(dialog).toBeVisible();
  await expect(input).toHaveValue(text);
  const opened = page.waitForEvent('popup');
  await dialog.getByRole('button', { name: 'Entrar novamente' }).click();
  const popup = await opened;
  // A sessão do provedor pode continuar ativa; sem ela, as credenciais são pedidas no tema InkLink.
  await Promise.race([
    popup.waitForURL(/\/bff\/session$/, { timeout: 60_000 }),
    popup.locator('#username').waitFor({ timeout: 60_000 }).then(() => submitCredentials(popup, 'client')),
  ]).catch(() => undefined);
  await expect(dialog).toBeHidden({ timeout: 60_000 });
  await expect(input).toHaveValue(text);
  await page.getByRole('button', { name: 'Enviar mensagem' }).click();
  await expect(page.getByRole('log').getByText(text, { exact: true })).toBeVisible();
  const history = (await api(page, 'GET', `/api/v1/conversations/${smoke.newConversation}/messages?limit=100`)).json as { items: { body: string }[] };
  expect(history.items.filter((item) => item.body === text)).toHaveLength(1);
});
