import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import type { Message } from '@/shared/api/contracts';
import { conversation, message } from '@/test/fixtures';
import { artist, client, page, problem, renderApp, signedIn } from '@/test/render';
import { server } from '@/test/server';

describe('conversas', () => {
  it('novo login após sessão expirada preserva o texto e reenvia com a mesma chave', async () => {
    signedIn(client);
    const keys: string[] = [];
    const history: Message[] = [];
    server.use(
      http.get('/api/v1/conversations/c1', () => HttpResponse.json(conversation())),
      http.get('/api/v1/conversations/c1/messages', () => HttpResponse.json(page([...history], { resumeCursor: 'resume' }))),
      http.get('/api/v1/conversations', () => HttpResponse.json(page([conversation()]))),
      http.post('/api/v1/conversations/c1/messages', async ({ request }) => {
        keys.push(request.headers.get('Idempotency-Key') ?? '');
        if (keys.length === 1) return problem(401, 'session.required');
        const body = (await request.json()) as { body: string };
        const created = message(1, client.id, body.body);
        history.push(created);
        return HttpResponse.json(created, { status: 201 });
      }),
    );
    const popup = { closed: false, close: vi.fn(() => { popup.closed = true; }) };
    vi.spyOn(window, 'open').mockImplementation(() => popup as unknown as Window);
    renderApp('/mensagens/c1');
    const input = await screen.findByLabelText('Mensagem');
    await userEvent.type(input, 'Texto que não pode se perder');
    await userEvent.click(screen.getByRole('button', { name: 'Enviar mensagem' }));
    const dialog = await screen.findByRole('dialog', { name: 'Sua sessão expirou' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Entrar novamente' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Sua sessão expirou' })).not.toBeInTheDocument(), { timeout: 4000 });
    expect(screen.getByLabelText('Mensagem')).toHaveValue('Texto que não pode se perder');
    await userEvent.click(screen.getByRole('button', { name: 'Enviar mensagem' }));
    expect(await within(await screen.findByRole('log')).findByText('Texto que não pode se perder')).toBeInTheDocument();
    expect(keys).toHaveLength(2);
    expect(keys[1]).toBe(keys[0]);
    expect(popup.close).toHaveBeenCalled();
  }, 15_000); // Aguarda a consulta real de /bff/session a cada 1,5 s.

  it('sessão que expira durante a atualização em segundo plano mantém a conversa e o rascunho', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      signedIn(client);
      let expired = false;
      server.use(
        http.get('/api/v1/conversations/c1', () => (expired ? problem(401, 'session.required') : HttpResponse.json(conversation()))),
        http.get('/api/v1/conversations/c1/messages', () =>
          expired ? problem(401, 'session.required') : HttpResponse.json(page([message(1, artist.id, 'Oi, Júlia!')], { resumeCursor: 'resume' })),
        ),
      );
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      renderApp('/mensagens/c1');
      const input = await screen.findByLabelText('Mensagem');
      expect(await screen.findByText('Oi, Júlia!')).toBeInTheDocument();
      await user.type(input, 'Rascunho em andamento');
      expired = true;
      // As atualizações periódicas do histórico (4 s) e da conversa (15 s) recebem 401.
      await vi.advanceTimersByTimeAsync(16_000);
      expect(await screen.findByRole('dialog', { name: 'Sua sessão expirou' })).toBeInTheDocument();
      expect(screen.getByLabelText('Mensagem')).toHaveValue('Rascunho em andamento');
      expect(screen.getByText('Oi, Júlia!')).toBeInTheDocument();
      expect(screen.queryByText('Não foi possível atualizar o histórico agora. Tentaremos novamente.')).not.toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it('falha na atualização em segundo plano mantém a conversa e avisa que os dados podem estar desatualizados', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      signedIn(client);
      let unavailable = false;
      server.use(
        http.get('/api/v1/conversations/c1', () => (unavailable ? problem(503, 'dependency.unavailable') : HttpResponse.json(conversation()))),
        http.get('/api/v1/conversations/c1/messages', () => HttpResponse.json(page([message(1, artist.id, 'Oi, Júlia!')], { resumeCursor: 'resume' }))),
      );
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      renderApp('/mensagens/c1');
      expect(await screen.findByText('Oi, Júlia!')).toBeInTheDocument();
      expect(screen.queryByText(/pode estar desatualizado/)).not.toBeInTheDocument();
      unavailable = true;
      await vi.advanceTimersByTimeAsync(16_000);
      expect(await screen.findByText(/pode estar desatualizado/)).toBeInTheDocument();
      expect(screen.getByLabelText('Mensagem')).toBeInTheDocument();
      unavailable = false;
      await user.click(screen.getByRole('button', { name: 'Atualizar agora' }));
      await waitFor(() => expect(screen.queryByText(/pode estar desatualizado/)).not.toBeInTheDocument());
    } finally {
      vi.useRealTimers();
    }
  });

  it('lista e leitura são páginas distintas e a listagem identifica o artista', async () => {
    signedIn(client);
    server.use(
      http.get('/api/v1/conversations', () =>
        HttpResponse.json(page([conversation({ lastMessage: message(1, client.id, 'Separei referências') }), conversation({ id: 'c2', engagementId: 'e2', engagementState: 'Closed', writingBlocked: true, counterpart: { id: 'x', displayName: 'Bruno Costa' } })])),
      ),
    );
    renderApp('/mensagens');
    const list = await screen.findByRole('list');
    expect(within(list).getByText('Rafael Moura')).toBeInTheDocument();
    expect(within(list).getByText('Separei referências')).toBeInTheDocument();
    expect(within(list).getByText('Serviço encerrado')).toBeInTheDocument();
    expect(screen.queryByRole('log')).not.toBeInTheDocument();
  });

  it('abre pelo endereço, distingue autoria e envia com chave idempotente', async () => {
    signedIn(client);
    const history: Message[] = [message(1, client.id, 'Oi, Rafael!'), message(2, artist.id, 'Oi, Júlia! Adorei a ideia.')];
    const keys: string[] = [];
    server.use(
      http.get('/api/v1/conversations/c1', () => HttpResponse.json(conversation())),
      http.get('/api/v1/conversations/c1/messages', ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor');
        return HttpResponse.json(page(cursor ? history.slice(2) : history, { resumeCursor: 'resume' }));
      }),
      http.post('/api/v1/conversations/c1/messages', async ({ request }) => {
        keys.push(request.headers.get('Idempotency-Key') ?? '');
        const body = (await request.json()) as { body: string };
        const created = message(3, client.id, body.body);
        history.push(created);
        return HttpResponse.json(created, { status: 201 });
      }),
      http.get('/api/v1/conversations', () => HttpResponse.json(page([conversation()]))),
    );
    renderApp('/mensagens/c1');
    expect(await screen.findByRole('heading', { name: 'Rafael Moura' })).toBeInTheDocument();
    const log = await screen.findByRole('log');
    expect(await within(log).findByText('Oi, Rafael!')).toBeInTheDocument();
    expect(within(log).getByText('Oi, Rafael!').closest('.bubble')).toHaveClass('mine');
    expect(within(log).getByText('Oi, Júlia! Adorei a ideia.').closest('.bubble')).not.toHaveClass('mine');

    const input = screen.getByLabelText('Mensagem');
    const send = screen.getByRole('button', { name: 'Enviar mensagem' });
    await userEvent.type(input, '   ');
    expect(send).toBeDisabled();
    await userEvent.clear(input);
    await userEvent.type(input, 'Sábado de manhã funciona?');
    await userEvent.click(send);
    expect(await within(log).findByText('Sábado de manhã funciona?')).toBeInTheDocument();
    expect(keys).toHaveLength(1);
    expect(input).toHaveValue('');
  });

  it('conversa encerrada mantém histórico e bloqueia o envio', async () => {
    signedIn(client);
    server.use(
      http.get('/api/v1/conversations/c1', () => HttpResponse.json(conversation({ writingBlocked: true, engagementState: 'Closed' }))),
      http.get('/api/v1/conversations/c1/messages', () => HttpResponse.json(page([message(1, client.id, 'Obrigada!')]))),
    );
    renderApp('/mensagens/c1');
    expect(await screen.findByText('Obrigada!')).toBeInTheDocument();
    expect(screen.getAllByText('Serviço encerrado').length).toBeGreaterThan(0);
    expect(screen.getByLabelText('Mensagem')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Enviar mensagem' })).toBeDisabled();
  });

  it('identificador inexistente ou de terceiro não expõe conteúdo e oferece retorno', async () => {
    signedIn(client);
    server.use(
      http.get('/api/v1/conversations/zz', () => problem(404, 'messaging.not_found')),
      http.get('/api/v1/conversations/zz/messages', () => problem(404, 'messaging.not_found')),
    );
    renderApp('/mensagens/zz');
    expect(await screen.findByText('Não encontramos este conteúdo.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver minhas conversas' })).toBeInTheDocument();
  });
});
