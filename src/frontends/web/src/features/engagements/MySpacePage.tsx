import { ArrowRight, CalendarDays, MessageCircle } from 'lucide-react';
import { Link } from 'react-router';
import { ArtistGrid } from '@/features/catalog/ArtistCardView';
import { useFavorites } from '@/features/catalog/api';
import { useConversations } from '@/features/messaging/api';
import { useCalendar } from '@/features/scheduling/api';
import { RequireSession } from '@/features/session/guards';
import type { Session } from '@/features/session/session';
import { addDays, todayIn } from '@/shared/lib/dates';
import { civilDate, engagementStateLabel, money, time } from '@/shared/lib/format';
import { Avatar } from '@/shared/ui/Avatar';
import { PageHeading, SectionHeading } from '@/shared/ui/PageHeading';
import { EmptyState, ErrorState, Loading } from '@/shared/ui/States';
import { useEngagements } from './api';

export function statusTone(state: string): string {
  return state === 'Agreed' ? 'status green' : state === 'Closed' ? 'status red' : state === 'Closing' ? 'status amber' : 'status';
}

function Requests() {
  const engagements = useEngagements('client');
  // Foto pública do artista, quando a conversa do atendimento já existe; sem ela, as iniciais.
  const conversations = useConversations();
  const photos = new Map((conversations.data ?? []).map((c) => [c.engagementId, c.counterpart.photoThumbnailUrl]));
  if (engagements.isPending) return <Loading label="Carregando seus pedidos…" />;
  if (engagements.isLoadingError) return <ErrorState error={engagements.error} onRetry={() => void engagements.refetch()} />;
  const items = [...engagements.data].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  return (
    <>
      <SectionHeading title="Orçamentos e atendimentos" count={items.length} />
      {items.length === 0 ? (
        <EmptyState title="Você ainda não enviou nenhum pedido.">
          <p>Encontre um artista e conte sua ideia para começar uma negociação.</p>
          <Link className="button red" to="/explorar">
            Explorar artistas
          </Link>
        </EmptyState>
      ) : (
        <ul className="list">
          {items.map((engagement) => (
            <li key={engagement.id}>
              <Link className="list-item" to={`/atendimentos/${engagement.id}`}>
                <Avatar className="avatar light" name={engagement.catalog.name} photoUrl={photos.get(engagement.id)} />
                <div className="grow">
                  <h3>{engagement.idea.length > 90 ? `${engagement.idea.slice(0, 90)}…` : engagement.idea}</h3>
                  <p>
                    {engagement.catalog.name} · {engagement.bodyRegion} · {engagement.approximateSizeCm} cm
                    {engagement.currentProposal && ` · proposta de ${money(engagement.currentProposal.amount)}`}
                  </p>
                </div>
                <span className={statusTone(engagement.state)}>
                  {engagement.state === 'Negotiating' && !engagement.currentProposal ? 'Aguardando proposta' : engagementStateLabel(engagement.state)}
                </span>
                <ArrowRight size={18} aria-hidden="true" />
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

function UpcomingReservations() {
  const today = todayIn();
  const reservations = useCalendar('client', today, addDays(today, 92));
  return (
    <>
      <SectionHeading eyebrow="SEUS PRÓXIMOS CAPÍTULOS" title="Sessões agendadas" count={reservations.data?.length} />
      {reservations.isPending ? (
        <Loading label="Carregando sua agenda…" />
      ) : reservations.isLoadingError ? (
        <ErrorState error={reservations.error} onRetry={() => void reservations.refetch()} />
      ) : reservations.data.length === 0 ? (
        <EmptyState title="Nenhuma sessão confirmada nos próximos 90 dias." icon={<CalendarDays size={26} aria-hidden="true" />}>
          <p>Depois do aceite da proposta, combine o horário pela conversa. O artista confirma a reserva na agenda dele.</p>
        </EmptyState>
      ) : (
        <ul className="list">
          {reservations.data.map((reservation) => (
            <li key={reservation.id}>
              <Link className="list-item" to={`/atendimentos/${reservation.engagementId}`}>
                <CalendarDays size={22} aria-hidden="true" />
                <div className="grow">
                  <h3>
                    {civilDate(reservation.localDate)} · {time(reservation.start, reservation.timeZoneId)}–{time(reservation.end, reservation.timeZoneId)}
                  </h3>
                  <p>
                    {reservation.artistName} · {reservation.project} · horário de {reservation.timeZoneId}
                  </p>
                </div>
                <span className="status green">Confirmada</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

function Favorites() {
  const favorites = useFavorites(true);
  return (
    <>
      <SectionHeading eyebrow="SUA PRÓXIMA INSPIRAÇÃO" title="Artistas que você salvou" count={favorites.data?.length}>
        <Link className="text-button" to="/explorar">
          Explorar mais <ArrowRight size={16} aria-hidden="true" />
        </Link>
      </SectionHeading>
      {favorites.isPending ? (
        <Loading label="Carregando favoritos…" />
      ) : favorites.isLoadingError ? (
        <ErrorState error={favorites.error} onRetry={() => void favorites.refetch()} />
      ) : favorites.data.length === 0 ? (
        <EmptyState title="Você ainda não salvou nenhum artista.">
          <p>Toque no coração de um artista para guardá-lo aqui.</p>
        </EmptyState>
      ) : (
        <ArtistGrid artists={favorites.data} />
      )}
    </>
  );
}

function Content({ session }: { session: Session }) {
  return (
    <>
      <PageHeading eyebrow="SEU ESPAÇO, SUAS HISTÓRIAS" title={`Oi, ${session.profile.displayName.split(' ')[0]}. Qual vai ser a próxima?`}>
        Suas ideias, seus artistas favoritos e seus próximos capítulos na pele.
      </PageHeading>
      <div className="row">
        <Link className="button outline" to="/mensagens">
          <MessageCircle size={16} aria-hidden="true" /> Minhas conversas
        </Link>
      </div>
      <Requests />
      <UpcomingReservations />
      <Favorites />
    </>
  );
}

export function MySpacePage() {
  return <RequireSession message="Entre para ver seus pedidos, conversas e favoritos.">{(session) => <Content session={session} />}</RequireSession>;
}
