import { ChevronRight, MessageCircle } from 'lucide-react';
import { Link } from 'react-router';
import { RequireArtist, RequireSession } from '@/features/session/guards';
import type { Session } from '@/features/session/session';
import type { Conversation } from '@/shared/api/contracts';
import { dateTime, engagementStateLabel } from '@/shared/lib/format';
import { Avatar } from '@/shared/ui/Avatar';
import { PageHeading, SectionHeading } from '@/shared/ui/PageHeading';
import { EmptyState, ErrorState, Loading } from '@/shared/ui/States';
import { useConversations } from './api';

type Perspective = 'client' | 'artist';

export function belongsTo(conversation: Conversation, perspective: Perspective, profileId: string): boolean {
  return perspective === 'client' ? conversation.clientId === profileId : conversation.artistAccountId === profileId;
}

export function conversationStatus(conversation: Conversation): { label: string; tone: string } {
  if (conversation.engagementState === 'Closed') return { label: 'Serviço encerrado', tone: 'status red' };
  if (conversation.engagementState === 'Closing') return { label: 'Encerramento em andamento', tone: 'status amber' };
  if (conversation.writingBlocked) return { label: 'Envio bloqueado', tone: 'status red' };
  return { label: `Conversa ativa · ${engagementStateLabel(conversation.engagementState)}`, tone: 'status green' };
}

function List({ session, perspective }: { session: Session; perspective: Perspective }) {
  const conversations = useConversations();
  const base = perspective === 'artist' ? '/estudio/mensagens' : '/mensagens';
  if (conversations.isPending) return <Loading label="Carregando conversas…" />;
  if (conversations.isError) return <ErrorState error={conversations.error} onRetry={() => void conversations.refetch()} />;
  const items = conversations.data
    .filter((c) => belongsTo(c, perspective, session.profile.id))
    .sort((a, b) => (b.lastMessage?.createdAt ?? b.createdAt).localeCompare(a.lastMessage?.createdAt ?? a.createdAt));

  return (
    <section aria-labelledby="conversations-title">
      <SectionHeading title="Suas conversas" count={items.length} />
      <h2 id="conversations-title" className="visually-hidden">
        Lista de conversas
      </h2>
      {items.length === 0 ? (
        <EmptyState title="Nenhuma conversa por aqui ainda." icon={<MessageCircle size={26} aria-hidden="true" />}>
          <p>
            {perspective === 'client'
              ? 'As conversas começam quando você envia um pedido de orçamento a um artista.'
              : 'As conversas começam quando um cliente envia um pedido para você.'}
          </p>
          {perspective === 'client' && (
            <Link className="button red" to="/explorar">
              Explorar artistas
            </Link>
          )}
        </EmptyState>
      ) : (
        <ul className="list">
          {items.map((conversation) => {
            const status = conversationStatus(conversation);
            return (
              <li key={conversation.id}>
                <Link className="list-item" to={`${base}/${conversation.id}`}>
                  <Avatar className="avatar light" name={conversation.counterpart.displayName} photoUrl={conversation.counterpart.photoThumbnailUrl} />
                  <div className="grow">
                    <h3>{conversation.counterpart.displayName}</h3>
                    <p>{conversation.lastMessage ? conversation.lastMessage.body : 'Nenhuma mensagem ainda.'}</p>
                    {conversation.lastMessage && <small className="muted">{dateTime(conversation.lastMessage.createdAt)}</small>}
                  </div>
                  <span className={status.tone}>{status.label}</span>
                  <ChevronRight size={18} aria-hidden="true" />
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

export function ConversationsPage({ perspective }: { perspective: Perspective }) {
  const heading = (
    <PageHeading eyebrow={perspective === 'artist' ? 'SEUS CLIENTES' : 'SUAS CONEXÕES'} title="Suas conversas. Suas próximas histórias.">
      {perspective === 'artist' ? 'Escolha um cliente para abrir a conversa do atendimento.' : 'Escolha um tatuador para abrir a conversa e acompanhar seu atendimento.'}
    </PageHeading>
  );
  return perspective === 'artist' ? (
    <RequireArtist>
      {(session) => (
        <>
          {heading}
          <List session={session} perspective="artist" />
        </>
      )}
    </RequireArtist>
  ) : (
    <RequireSession message="Entre para ver suas conversas.">
      {(session) => (
        <>
          {heading}
          <List session={session} perspective="client" />
        </>
      )}
    </RequireSession>
  );
}
