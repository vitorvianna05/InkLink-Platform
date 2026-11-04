import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Engagement, LifecycleStatus } from '@/shared/api/contracts';
import { conversation, engagement, proposal } from '@/test/fixtures';
import { artist, client, page, problem, renderApp, signedIn } from '@/test/render';
import { server } from '@/test/server';

function backend(current: () => Engagement, lifecycle: () => LifecycleStatus = () => ({ requiresReservation: false, state: current().state, operations: [] })) {
  server.use(
    http.get('/api/v2/engagements/e1', () => HttpResponse.json(current())),
    http.get('/api/v2/engagements/e1/proposals', () => HttpResponse.json(page(current().currentProposal ? [current().currentProposal] : []))),
    http.get('/api/v2/engagements/e1/lifecycle', () => HttpResponse.json(lifecycle())),
    http.get('/api/v1/conversations', () => HttpResponse.json(page([conversation()]))),
    http.get('/api/v1/scheduling/calendar', () => HttpResponse.json(page([]))),
  );
}

describe('negociação', () => {
  it('cliente aceita a proposta vigente somente após confirmação explícita', async () => {
    signedIn(client);
    let state = engagement();
    let accepted = 0;
    backend(() => state);
    server.use(
      http.put('/api/v2/engagements/e1/proposals/p1/acceptance', () => {
        accepted++;
        state = engagement({ state: 'Agreed', currentProposal: { ...proposal, state: 'Accepted', acceptedAt: '2026-11-02T10:00:00Z' } });
        return HttpResponse.json(state.currentProposal);
      }),
    );
    renderApp('/atendimentos/e1');
    await userEvent.click(await screen.findByRole('button', { name: 'Aceitar proposta' }));
    const dialog = screen.getByRole('dialog', { name: 'Confirmar o aceite?' });
    expect(within(dialog).getByText(/Nenhuma cobrança é feita/)).toBeInTheDocument();
    expect(accepted).toBe(0);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirmar aceite' }));
    expect(await screen.findAllByText('Proposta aceita')).not.toHaveLength(0);
    expect(accepted).toBe(1);
    expect(screen.queryByRole('button', { name: 'Aceitar proposta' })).not.toBeInTheDocument();
  });

  it('aceite de proposta substituída informa o conflito', async () => {
    signedIn(client);
    backend(() => engagement());
    server.use(http.put('/api/v2/engagements/e1/proposals/p1/acceptance', () => problem(409, 'engagements.proposal_not_current')));
    renderApp('/atendimentos/e1');
    await userEvent.click(await screen.findByRole('button', { name: 'Aceitar proposta' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar aceite' }));
    expect(await screen.findByText(/Esta proposta foi substituída/)).toBeInTheDocument();
  });

  it('artista apresenta proposta com validação de valor', async () => {
    signedIn(artist, true);
    let sent: unknown = null;
    backend(() => engagement({ currentProposal: null, currentProposalId: null }));
    server.use(
      http.post('/api/v2/engagements/e1/proposals', async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json(proposal, { status: 201 });
      }),
    );
    renderApp('/estudio/atendimentos/e1');
    await userEvent.type(await screen.findByLabelText('Descrição'), 'Desenho exclusivo');
    await userEvent.type(screen.getByLabelText('Valor (R$)'), '0');
    await userEvent.click(screen.getByRole('button', { name: 'Enviar proposta' }));
    expect(screen.getByText(/Informe um valor maior que zero/)).toBeInTheDocument();
    await userEvent.clear(screen.getByLabelText('Valor (R$)'));
    await userEvent.type(screen.getByLabelText('Valor (R$)'), '1.250,50');
    await userEvent.click(screen.getByRole('button', { name: 'Enviar proposta' }));
    expect(await screen.findByText('Proposta enviada ao cliente.')).toBeInTheDocument();
    expect(sent).toEqual({ description: 'Desenho exclusivo', amount: 1250.5 });
  });
});

describe('encerramento e novo atendimento', () => {
  it('distingue encerramento pendente de definitivo', async () => {
    signedIn(client);
    let state: Engagement = engagement({ state: 'Agreed' });
    let lifecycle: LifecycleStatus = { requiresReservation: false, state: 'Agreed', operations: [] };
    backend(
      () => state,
      () => lifecycle,
    );
    server.use(
      http.post('/api/v2/engagements/e1/closure', async ({ request }) => {
        expect(await request.json()).toEqual({ confirmed: true });
        state = engagement({ state: 'Closing' });
        const op = { id: 'o1', engagementId: 'e1', kind: 'Close', status: 'Pending', attempts: 1, error: null, updatedAt: '2026-11-03T10:00:00Z' } as const;
        lifecycle = { requiresReservation: false, state: 'Closing', operations: [op] };
        return HttpResponse.json(op, { status: 202 });
      }),
    );
    renderApp('/atendimentos/e1');
    await userEvent.click(await screen.findByRole('button', { name: 'Encerrar serviço' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar encerramento' }));
    expect(await screen.findByRole('heading', { name: 'Encerramento em andamento' })).toBeInTheDocument();
    expect(screen.getByText(/ainda não é definitivo/)).toBeInTheDocument();
  });

  it('operação em quarentena pode ser retomada e reservas futuras explicam a recusa', async () => {
    signedIn(client);
    let retried = false;
    backend(
      () => engagement({ state: 'Agreed' }),
      () => ({
        requiresReservation: true,
        state: 'Agreed',
        operations: [
          { id: 'o1', engagementId: 'e1', kind: 'Close', status: 'Rejected', attempts: 1, error: 'engagements.future_reservations', updatedAt: '2026-11-03T10:00:00Z' },
          { id: 'o2', engagementId: 'e1', kind: 'Activate', status: 'Quarantined', attempts: 10, error: 'messaging.unavailable', updatedAt: '2026-11-03T11:00:00Z' },
        ],
      }),
    );
    server.use(
      http.post('/api/v2/engagements/e1/lifecycle/retry', () => {
        retried = true;
        return new HttpResponse(null, { status: 202 });
      }),
    );
    renderApp('/atendimentos/e1');
    expect(await screen.findByText(/Há sessões com término futuro/)).toBeInTheDocument();
    expect(screen.getByText(/a conversa só é liberada depois que o artista confirmar/i)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Tentar novamente' }));
    expect(retried).toBe(true);
  });
});
