import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Dashboard } from '@/shared/api/contracts';
import { conversation, engagement } from '@/test/fixtures';
import { artist, client, page, problem, renderApp, signedIn } from '@/test/render';
import { server } from '@/test/server';

const dashboard: Dashboard = {
  from: '2026-11-01',
  to: '2026-12-01',
  timeZoneId: 'America/Sao_Paulo',
  financial: { received: 4000, costs: 940, estimatedProfit: 3060, estimatedMargin: 76.5 },
  completedSessions: 3,
  clientsServed: 2,
  averageSessionValue: 500,
  scheduledReservations: 1,
  scheduledValue: 450,
  currentActiveConversations: 4,
  scheduleObservedAt: '2026-11-04T00:00:00Z',
  conversationsObservedAt: '2026-11-04T00:00:00Z',
  generatedAt: '2026-11-04T00:00:00Z',
  currency: 'BRL',
  conversationBasis: 'Current, independent of financial period',
};

describe('painel do artista', () => {
  it('exibe indicadores do backend e acesso ao atendimento do cliente', async () => {
    signedIn(artist, true);
    server.use(
      http.get('/api/v1/finance/dashboard', () => HttpResponse.json(dashboard)),
      http.get('/api/v2/engagements', () => HttpResponse.json(page([engagement()]))),
      http.get('/api/v1/conversations', () => HttpResponse.json(page([conversation({ counterpart: { id: client.id, displayName: 'Júlia Vieira' } })]))),
    );
    renderApp('/estudio');
    expect(await screen.findByText('R$ 4.000,00')).toBeInTheDocument();
    expect(screen.getByText('R$ 940,00')).toBeInTheDocument();
    expect(screen.getByText('R$ 3.060,00')).toBeInTheDocument();
    expect(screen.getByText('76,5%')).toBeInTheDocument();
    const link = await screen.findByRole('link', { name: /Júlia Vieira/ });
    expect(link).toHaveAttribute('href', '/estudio/atendimentos/e1');
  });

  it('falha de fonte aparece como erro, não como zero', async () => {
    signedIn(artist, true);
    server.use(
      http.get('/api/v1/finance/dashboard', () => problem(503, 'finance.source_unavailable')),
      http.get('/api/v2/engagements', () => HttpResponse.json(page([]))),
      http.get('/api/v1/conversations', () => HttpResponse.json(page([]))),
    );
    renderApp('/estudio');
    expect(await screen.findByText('Serviço temporariamente indisponível.')).toBeInTheDocument();
    expect(screen.queryByText('R$ 0,00')).not.toBeInTheDocument();
  });

  it('período sem movimentação é identificado como vazio', async () => {
    signedIn(artist, true);
    const zero = { ...dashboard, financial: { received: 0, costs: 0, estimatedProfit: 0, estimatedMargin: 0 }, completedSessions: 0, clientsServed: 0, averageSessionValue: 0, scheduledReservations: 0, scheduledValue: 0 };
    server.use(
      http.get('/api/v1/finance/dashboard', () => HttpResponse.json(zero)),
      http.get('/api/v2/engagements', () => HttpResponse.json(page([]))),
      http.get('/api/v1/conversations', () => HttpResponse.json(page([]))),
    );
    renderApp('/estudio');
    expect(await screen.findByText(/Nenhuma movimentação registrada/)).toBeInTheDocument();
  });
});
