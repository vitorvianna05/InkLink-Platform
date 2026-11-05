import { ArrowLeft, CalendarDays, MessageCircle } from 'lucide-react';
import { Link, useLocation, useParams } from 'react-router';
import { useConversations } from '@/features/messaging/api';
import { useCalendar } from '@/features/scheduling/api';
import { RequireArtist, RequireSession } from '@/features/session/guards';
import type { Engagement } from '@/shared/api/contracts';
import { addDays, todayIn } from '@/shared/lib/dates';
import { civilDate, engagementStateLabel, money, time } from '@/shared/lib/format';
import { Avatar } from '@/shared/ui/Avatar';
import { ErrorState, Loading } from '@/shared/ui/States';
import { useEngagement, type Perspective } from './api';
import { LifecycleSection } from './LifecycleSection';
import { statusTone } from './MySpacePage';
import { ProposalSection } from './ProposalSection';

function ConversationLink({ engagement, perspective }: { engagement: Engagement; perspective: Perspective }) {
  const conversations = useConversations();
  if (conversations.isPending) return <Loading label="Procurando a conversa…" />;
  if (conversations.isError) return <ErrorState error={conversations.error} onRetry={() => void conversations.refetch()} />;
  const conversation = conversations.data.find((c) => c.engagementId === engagement.id);
  if (!conversation) {
    return (
      <p className="notice" role="status">
        A conversa deste atendimento ainda não está disponível. Ela é aberta automaticamente após o pedido{' '}
        {engagement.state === 'Negotiating' ? 'e aparece aqui em instantes.' : 'ou após a confirmação de uma nova reserva.'}
      </p>
    );
  }
  const base = perspective === 'artist' ? '/estudio/mensagens' : '/mensagens';
  return (
    <Link className="button dark" to={`${base}/${conversation.id}`}>
      <MessageCircle size={16} aria-hidden="true" /> {conversation.writingBlocked ? 'Ver histórico da conversa' : 'Abrir conversa'}
    </Link>
  );
}

/** Interlocutor do atendimento, com a foto entregue pela conversa (decisão 0018); sem conversa, nome e iniciais. */
function Counterpart({ engagement, perspective }: { engagement: Engagement; perspective: Perspective }) {
  const conversations = useConversations();
  const counterpart = conversations.data?.find((c) => c.engagementId === engagement.id)?.counterpart;
  const name = counterpart?.displayName ?? (perspective === 'artist' ? 'Cliente' : engagement.catalog.name);
  return (
    <p className="row counterpart">
      <Avatar className="avatar small" name={name} photoUrl={counterpart?.photoThumbnailUrl} />
      <span>
        {perspective === 'artist' ? 'Cliente' : 'Artista'}: <strong>{name}</strong>
      </span>
    </p>
  );
}

function EngagementReservations({ engagement, perspective }: { engagement: Engagement; perspective: Perspective }) {
  const from = addDays(todayIn(), -31);
  const reservations = useCalendar(perspective, from, addDays(from, 93));
  const items = (reservations.data ?? []).filter((r) => r.engagementId === engagement.id);
  return (
    <section className="side-panel" aria-labelledby="sessions-title">
      <span className="eyebrow">AGENDA</span>
      <h2 id="sessions-title">Sessões</h2>
      {reservations.isPending ? (
        <Loading />
      ) : reservations.isError ? (
        <ErrorState error={reservations.error} onRetry={() => void reservations.refetch()} />
      ) : items.length === 0 ? (
        <p className="muted">
          Nenhuma sessão confirmada entre {civilDate(from)} e {civilDate(addDays(from, 92))}.
          {perspective === 'client' && engagement.state === 'Agreed' && ' Combine o horário pela conversa; o artista confirma a reserva na agenda dele.'}
        </p>
      ) : (
        <ul className="list">
          {items.map((r) => (
            <li key={r.id} className="list-item">
              <CalendarDays size={18} aria-hidden="true" />
              <div className="grow">
                <strong>
                  {civilDate(r.localDate)} · {time(r.start, r.timeZoneId)}–{time(r.end, r.timeZoneId)}
                </strong>
                <p>
                  Referência {money(r.referenceAmount)} · {r.timeZoneId}
                </p>
              </div>
            </li>
          ))}
        </ul>
      )}
      {perspective === 'artist' && engagement.state === 'Agreed' && (
        <Link className="button outline" to={`/estudio/agenda?atendimento=${engagement.id}`}>
          <CalendarDays size={16} aria-hidden="true" /> Agendar sessão
        </Link>
      )}
    </section>
  );
}

function Content({ perspective }: { perspective: Perspective }) {
  const { engagementId = '' } = useParams();
  const location = useLocation();
  const engagement = useEngagement(engagementId);
  const backTo = perspective === 'artist' ? '/estudio' : '/meu-espaco';
  const back = (
    <Link className="text-button" to={backTo}>
      <ArrowLeft size={16} aria-hidden="true" /> {perspective === 'artist' ? 'Voltar ao estúdio' : 'Voltar para Meu espaço'}
    </Link>
  );

  if (engagement.isPending) return <Loading label="Carregando atendimento…" />;
  if (engagement.isError) {
    return (
      <>
        {back}
        <ErrorState error={engagement.error} onRetry={() => void engagement.refetch()} />
      </>
    );
  }
  const data = engagement.data;
  const created = (location.state as { created?: boolean } | null)?.created;

  return (
    <article>
      {back}
      {created && (
        <p className="notice success" role="status" style={{ marginTop: 12 }}>
          Sua ideia foi enviada a {data.catalog.name}. Acompanhe a negociação por aqui.
        </p>
      )}
      <div className="page-heading">
        <span className="eyebrow">{perspective === 'artist' ? 'PEDIDO DE CLIENTE' : `ATENDIMENTO COM ${data.catalog.name.toUpperCase()}`}</span>
        <h1 style={{ fontSize: 'clamp(26px, 4vw, 40px)' }}>{data.idea}</h1>
        <p className="row">
          <span className={statusTone(data.state)}>{engagementStateLabel(data.state)}</span>
          <span>
            {data.bodyRegion} · {data.approximateSizeCm} cm · {data.catalog.studio}, {data.catalog.city}
          </span>
        </p>
        <Counterpart engagement={data} perspective={perspective} />
      </div>
      {data.catalog.flash && (
        <figure className="list-item" style={{ marginBottom: 20 }}>
          <img src={data.catalog.flash.imageUrl} alt={data.catalog.flash.alternativeText} width={96} height={96} style={{ objectFit: 'cover', borderRadius: 12 }} />
          <figcaption className="grow">
            <strong>Flash escolhido: {data.catalog.flash.title}</strong>
            <p>
              {data.catalog.flash.style}
              {data.catalog.flash.referencePrice !== null && ` · referência ${money(data.catalog.flash.referencePrice)}`}
            </p>
          </figcaption>
        </figure>
      )}
      <div className="conversation-layout">
        <div className="stack">
          <ConversationLink engagement={data} perspective={perspective} />
          <EngagementReservations engagement={data} perspective={perspective} />
          <LifecycleSection engagement={data} perspective={perspective} />
        </div>
        <ProposalSection engagement={data} perspective={perspective} />
      </div>
    </article>
  );
}

export function EngagementPage({ perspective }: { perspective: Perspective }) {
  return perspective === 'artist' ? (
    <RequireArtist>{() => <Content perspective="artist" />}</RequireArtist>
  ) : (
    <RequireSession message="Entre para acompanhar este atendimento.">{() => <Content perspective="client" />}</RequireSession>
  );
}
