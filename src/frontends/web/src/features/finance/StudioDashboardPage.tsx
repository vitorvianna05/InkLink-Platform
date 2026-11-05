import { ArrowRight, ArrowUpRight, CalendarDays, Wallet } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { useEngagements } from '@/features/engagements/api';
import { statusTone } from '@/features/engagements/MySpacePage';
import { useConversations } from '@/features/messaging/api';
import { RequireArtist } from '@/features/session/guards';
import type { Session } from '@/features/session/session';
import { todayIn } from '@/shared/lib/dates';
import { civilDate, dateTime, engagementStateLabel, money, percentage } from '@/shared/lib/format';
import { Avatar } from '@/shared/ui/Avatar';
import { PageHeading, SectionHeading } from '@/shared/ui/PageHeading';
import { EmptyState, ErrorState, Loading } from '@/shared/ui/States';
import { useDashboard } from './api';
import { monthPeriod, PeriodPicker, periodDays, type Period } from './PeriodPicker';

function Indicators({ period }: { period: Period }) {
  const valid = periodDays(period) >= 1 && periodDays(period) <= 366;
  const dashboard = useDashboard(period.from, period.to, valid);
  if (!valid) return null;
  if (dashboard.isPending) return <Loading label="Calculando indicadores…" />;
  if (dashboard.isError) return <ErrorState error={dashboard.error} onRetry={() => void dashboard.refetch()} />;
  const d = dashboard.data;
  const empty = d.financial.received === 0 && d.financial.costs === 0 && d.completedSessions === 0 && d.scheduledReservations === 0;
  return (
    <section aria-labelledby="indicators-title">
      <h2 id="indicators-title" className="visually-hidden">
        Indicadores do período
      </h2>
      {empty && (
        <p className="notice" role="status">
          Nenhuma movimentação registrada entre {civilDate(d.from)} e o dia anterior a {civilDate(d.to)}. Os valores abaixo estão zerados.
        </p>
      )}
      <div className="stats">
        <div className="stat dark">
          <span>Receita recebida</span>
          <strong>{money(d.financial.received)}</strong>
        </div>
        <div className="stat">
          <span>Custos registrados</span>
          <strong>{money(d.financial.costs)}</strong>
        </div>
        <div className="stat">
          <span>Lucro estimado</span>
          <strong>{money(d.financial.estimatedProfit)}</strong>
        </div>
        <div className="stat">
          <span>Margem estimada</span>
          <strong>{percentage(d.financial.estimatedMargin)}</strong>
        </div>
        <div className="stat">
          <span>Clientes atendidos</span>
          <strong>{d.clientsServed}</strong>
        </div>
        <div className="stat">
          <span>Sessões concluídas</span>
          <strong>{d.completedSessions}</strong>
        </div>
        <div className="stat">
          <span>Ticket médio por sessão</span>
          <strong>{money(d.averageSessionValue)}</strong>
        </div>
        <div className="stat">
          <span>Reservas a realizar · valor agendado</span>
          <strong>
            {d.scheduledReservations} · {money(d.scheduledValue)}
          </strong>
        </div>
        <div className="stat">
          <span>Conversas ativas agora</span>
          <strong>{d.currentActiveConversations}</strong>
        </div>
      </div>
      <p className="muted" style={{ fontSize: 13 }}>
        Lucro e margem são estimativas: receita recebida menos todos os custos registrados no período. Valor agendado soma referências de reservas ainda sem conclusão e não é
        receita. Conversas ativas são a quantidade atual, independente do período. Agenda observada em {dateTime(d.scheduleObservedAt)}; conversas em{' '}
        {dateTime(d.conversationsObservedAt)}; fuso {d.timeZoneId}.
      </p>
    </section>
  );
}

function Clients() {
  const engagements = useEngagements('artist');
  const conversations = useConversations();
  if (engagements.isPending) return <Loading label="Carregando atendimentos…" />;
  if (engagements.isError) return <ErrorState error={engagements.error} onRetry={() => void engagements.refetch()} />;
  const names = new Map((conversations.data ?? []).map((c) => [c.engagementId, c.counterpart.displayName]));
  const items = [...engagements.data].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  if (items.length === 0) return <EmptyState title="Nenhum pedido recebido ainda." />;
  return (
    <ul className="list">
      {items.map((e) => (
        <li key={e.id}>
          <Link className="list-item" to={`/estudio/atendimentos/${e.id}`}>
            <div className="grow">
            <Avatar className="avatar light" name={counterparts.get(e.id)?.displayName ?? 'Cliente'} photoUrl={counterparts.get(e.id)?.photoThumbnailUrl} />
              <h3>{counterparts.get(e.id)?.displayName ?? 'Cliente'}</h3>
              <p>
                {e.idea.length > 80 ? `${e.idea.slice(0, 80)}…` : e.idea} · {e.bodyRegion} · {e.approximateSizeCm} cm
              </p>
            </div>
            <span className={statusTone(e.state)}>
              {e.state === 'Negotiating' && !e.currentProposal ? 'Aguardando sua proposta' : engagementStateLabel(e.state)}
            </span>
            <ArrowRight size={18} aria-hidden="true" />
          </Link>
        </li>
      ))}
    </ul>
  );
}

function Content({ session }: { session: Session }) {
  const [period, setPeriod] = useState<Period>(() => monthPeriod(todayIn(session.artistTimeZone ?? undefined)));
  return (
    <>
      <PageHeading eyebrow={`ÁREA DO ARTISTA · ${session.profile.displayName.toUpperCase()}`} title="Seu estúdio, em um só lugar.">
        Indicadores do período, clientes e atalhos para agenda, finanças e portfólio.
      </PageHeading>
      <div className="row" style={{ marginBottom: 20 }}>
        <Link className="button outline" to="/estudio/agenda">
          <CalendarDays size={16} aria-hidden="true" /> Agenda
        </Link>
        <Link className="button outline" to="/estudio/financeiro">
          <Wallet size={16} aria-hidden="true" /> Lançamentos
        </Link>
        <Link className="button outline" to="/estudio/portfolio">
          Ver e editar perfil público <ArrowUpRight size={16} aria-hidden="true" />
        </Link>
      </div>
      <div className="form-card" style={{ marginBottom: 20 }}>
        <PeriodPicker value={period} onChange={setPeriod} />
      </div>
      <Indicators period={period} />
      <SectionHeading eyebrow="NOVAS CONEXÕES" title="Clientes e atendimentos" />
      <Clients />
    </>
  );
}

export function StudioDashboardPage() {
  return <RequireArtist>{(session) => <Content session={session} />}</RequireArtist>;
}
