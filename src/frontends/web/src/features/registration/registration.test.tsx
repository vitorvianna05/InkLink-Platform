import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { browser } from '@/shared/lib/browser';
import { anonymous, client, page, problem, renderApp, signedIn } from '@/test/render';
import { server } from '@/test/server';
import type { Registration } from './api';

const pendingRegistration = (overrides: Partial<Registration> = {}): Registration => ({
  state: 'Pending',
  email: 'nova@example.test',
  emailConfirmed: false,
  phoneNumber: null,
  phoneConfirmed: false,
  termsVersion: '2026-11-provisorio',
  termsAccepted: false,
  artistRequest: 'None',
  emailResendAt: null,
  phoneResendAt: null,
  ...overrides,
});

/** Conta do cadastro ainda pendente: /profiles/me responde 403 account.confirmation_required (decisão 0020). */
function pendingAccount(registration: () => Registration, activeAfter: () => boolean = () => false) {
  server.use(
    http.get('/bff/session', () => HttpResponse.json({ authenticated: true, name: 'Nova Pessoa' })),
    http.get('/bff/antiforgery', () => HttpResponse.json({ requestToken: 'csrf-token' })),
    http.get('/api/v1/profiles/me', () => (activeAfter() ? HttpResponse.json({ id: 'p-new', displayName: 'Nova Pessoa' }) : problem(403, 'account.confirmation_required'))),
    http.get('/api/v1/scheduling/artist-context', () => problem(403, 'authorization.denied')),
    http.get('/api/v1/registration', () => HttpResponse.json(registration())),
  );
}

describe('cadastro e confirmação de contato (T10.24)', () => {
  it('conta pendente é levada da área privada à confirmação, com o destino preservado', async () => {
    pendingAccount(() => pendingRegistration());
    renderApp('/meu-espaco');
    expect(await screen.findByRole('heading', { level: 1, name: 'Confirme seu cadastro.' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: /Termos de uso/ })).toBeInTheDocument();
    expect(screen.getByText('nova@example.test')).toBeInTheDocument();
  });

  it('aceita os termos, confirma e-mail e celular por código e volta ao destino com a conta ativa', async () => {
    let state = pendingRegistration();
    const calls: Array<{ path: string; body: unknown }> = [];
    pendingAccount(() => state, () => state.state === 'Active');
    const update = (changes: Partial<Registration>) => {
      state = { ...state, ...changes };
      const done = state.termsAccepted && state.emailConfirmed && state.phoneConfirmed;
      if (done) state = { ...state, state: 'Active' };
      return HttpResponse.json(state);
    };
    server.use(
      http.put('/api/v1/registration/terms', async ({ request }) => {
        calls.push({ path: 'terms', body: await request.json() });
        return update({ termsAccepted: true });
      }),
      http.put('/api/v1/registration/phone', async ({ request }) => {
        calls.push({ path: 'phone', body: await request.json() });
        return update({ phoneNumber: '+5511987654321' });
      }),
      http.post('/api/v1/registration/codes', async ({ request }) => {
        const body = (await request.json()) as { channel: string };
        calls.push({ path: 'codes', body });
        return HttpResponse.json(update(body.channel === 'email' ? {} : {}) && state, { status: 202 });
      }),
      http.post('/api/v1/registration/codes/verify', async ({ request }) => {
        const body = (await request.json()) as { channel: string; code: string };
        calls.push({ path: 'verify', body });
        if (body.code !== '12345678') return problem(400, 'registration.code_invalid');
        return update(body.channel === 'email' ? { emailConfirmed: true } : { phoneConfirmed: true });
      }),
      http.get('/api/v2/engagements', () => HttpResponse.json(page([]))),
      http.get('/api/v1/scheduling/calendar', () => HttpResponse.json(page([]))),
      http.get('/api/v1/favorites', () => HttpResponse.json(page([]))),
      http.get('/api/v1/conversations', () => HttpResponse.json(page([]))),
    );
    renderApp('/cadastro/confirmar?destino=%2Fmeu-espaco');

    const terms = await screen.findByRole('checkbox', { name: /Termos de uso/ });
    const accept = screen.getByRole('button', { name: 'Aceitar termos' });
    expect(accept).toBeDisabled();
    await userEvent.click(terms);
    await userEvent.click(accept);
    expect(await screen.findByText('Versão 2026-11-provisorio aceita.')).toBeInTheDocument();
    expect(calls).toContainEqual({ path: 'terms', body: { version: '2026-11-provisorio' } });

    // E-mail: código errado informa o erro; o campo só aceita dígitos.
    const email = screen.getByRole('region', { name: /E-mail/ });
    await userEvent.click(within(email).getByRole('button', { name: 'Enviar código' }));
    expect(await within(email).findByText(/Enviamos um código de 8 dígitos para o seu e-mail/)).toBeInTheDocument();
    const emailCode = within(email).getByLabelText('Código do e-mail');
    expect(emailCode).toHaveAttribute('autocomplete', 'one-time-code');
    expect(emailCode).toHaveAttribute('inputmode', 'numeric');
    await userEvent.type(emailCode, '1234abc5679');
    expect(emailCode).toHaveValue('12345679');
    await userEvent.click(within(email).getByRole('button', { name: 'Confirmar' }));
    expect(await within(email).findByRole('alert')).toHaveTextContent('Código inválido');
    await userEvent.clear(emailCode);
    await userEvent.type(emailCode, '12345678');
    await userEvent.click(within(email).getByRole('button', { name: 'Confirmar' }));
    expect(await screen.findByRole('heading', { name: /E-mail \(concluído\)/ })).toBeInTheDocument();

    // Celular: salvo e normalizado pelo backend, depois confirmado.
    const phone = screen.getByRole('region', { name: /Celular/ });
    await userEvent.type(within(phone).getByLabelText('Celular com DDD'), '(11) 98765-4321');
    await userEvent.click(within(phone).getByRole('button', { name: 'Salvar celular' }));
    await userEvent.click(await within(phone).findByRole('button', { name: 'Enviar código' }));
    await userEvent.type(within(phone).getByLabelText('Código do celular'), '12345678');
    await userEvent.click(within(phone).getByRole('button', { name: 'Confirmar' }));

    expect(await screen.findByText('Cadastro concluído. Sua conta está pronta para usar.')).toBeInTheDocument();
    const next = screen.getByRole('button', { name: 'Continuar' });
    await waitFor(() => expect(next).toBeEnabled());
    await userEvent.click(next);
    expect(await screen.findByRole('heading', { name: /Oi, Nova/ })).toBeInTheDocument();
    expect(calls.filter((call) => call.path === 'codes').map((call) => call.body)).toEqual([{ channel: 'email' }, { channel: 'phone' }]);
  }, 15_000); // Fluxo completo com três etapas.


  it('informa o limite de reenvio e mostra a contagem até liberar outro código', async () => {
    const resendAt = new Date(Date.now() + 45_000).toISOString();
    let state = pendingRegistration({ termsAccepted: true });
    pendingAccount(() => state);
    server.use(
      http.post('/api/v1/registration/codes', () => {
        state = { ...state, emailResendAt: resendAt };
        return problem(429, 'registration.resend_too_soon');
      }),
    );
    renderApp('/cadastro/confirmar');
    const email = await screen.findByRole('region', { name: /E-mail/ });
    await userEvent.click(within(email).getByRole('button', { name: 'Enviar código' }));
    expect(await within(email).findByRole('alert')).toHaveTextContent('Aguarde alguns segundos');
    expect(await within(email).findByText(/Reenvio disponível em \d+ s/)).toBeInTheDocument();
    expect(within(email).getByRole('button', { name: 'Reenviar código' })).toBeDisabled();
  });

  it('a página de login oferece o cadastro pelo provedor, com retorno à confirmação', async () => {
    anonymous();
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => undefined);
    renderApp('/entrar?destino=%2Fmeu-espaco');
    await userEvent.click(await screen.findByRole('button', { name: 'Criar conta' }));
    expect(assign).toHaveBeenCalledWith(`/bff/register?returnUrl=${encodeURIComponent('/cadastro/confirmar?destino=%2Fmeu-espaco')}`);
    expect(screen.getByText('Abrindo a página segura de cadastro do InkLink.')).toBeInTheDocument();
  });

  it('cliente ativo solicita a função de artista e acompanha a análise em Meu espaço', async () => {
    signedIn(client);
    let requested = false;
    server.use(
      http.get('/api/v1/registration', () => HttpResponse.json(pendingRegistration({ state: 'Active', emailConfirmed: true, phoneConfirmed: true, termsAccepted: true, artistRequest: requested ? 'Pending' : 'None' }))),
      http.post('/api/v1/registration/artist-request', () => {
        requested = true;
        return HttpResponse.json(pendingRegistration({ state: 'Active', artistRequest: 'Pending' }));
      }),
      http.get('/api/v2/engagements', () => HttpResponse.json(page([]))),
      http.get('/api/v1/scheduling/calendar', () => HttpResponse.json(page([]))),
      http.get('/api/v1/favorites', () => HttpResponse.json(page([]))),
      http.get('/api/v1/conversations', () => HttpResponse.json(page([]))),
    );
    renderApp('/meu-espaco');
    await userEvent.click(await screen.findByRole('button', { name: /Quero ser artista/ }));
    expect(await screen.findByRole('heading', { name: 'Solicitação em análise' })).toBeInTheDocument();
    expect(screen.getByText('Em análise')).toBeInTheDocument();
  });
});
