import { ArrowLeft, ArrowUpRight, Check, Send } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router';
import { RequireArtist, RequireSession } from '@/features/session/guards';
import type { Session } from '@/features/session/session';
import { ApiError, newIdempotencyKey } from '@/shared/api/http';
import { dateTime } from '@/shared/lib/format';
import { Avatar } from '@/shared/ui/Avatar';
import { describeError, EmptyState, ErrorState, Loading } from '@/shared/ui/States';
import { useConversation, useMessageHistory, useSendMessage } from './api';
import { belongsTo, conversationStatus } from './ConversationsPage';

type Perspective = 'client' | 'artist';

function Composer({ conversationId, blocked, onSent }: { conversationId: string; blocked: boolean; onSent: () => void }) {
  const send = useSendMessage(conversationId);
  const [draft, setDraft] = useState('');
  const [key, setKey] = useState(newIdempotencyKey);
  const empty = draft.trim().length === 0;

  return (
    <form
      className="chat-form"
      onSubmit={(event) => {
        event.preventDefault();
        if (blocked || empty) return;
        send.mutate(
          { body: draft.trim(), key },
          {
            onSuccess: () => {
              setDraft('');
              setKey(newIdempotencyKey());
              onSent();
            },
            onError: (error) => {
              if (error instanceof ApiError && !error.isTransient && !error.isSessionExpired) setKey(newIdempotencyKey());
            },
          },
        );
      }}
    >
      <label className="visually-hidden" htmlFor="message-input">
        Mensagem
      </label>
      <input
        id="message-input"
        value={draft}
        maxLength={4000}
        disabled={blocked || send.isPending}
        placeholder={blocked ? 'Chat encerrado — o histórico continua disponível' : 'Escreva sua mensagem…'}
        onChange={(event) => {
          setDraft(event.target.value);
          if (send.isError) send.reset();
        }}
        aria-describedby={send.isError ? 'send-error' : undefined}
      />
      <button type="submit" className="button red" disabled={blocked || empty || send.isPending} aria-label="Enviar mensagem">
        <Send size={18} aria-hidden="true" />
      </button>
      {send.isError && (
        <p id="send-error" className="field-error" role="alert" style={{ flexBasis: '100%', margin: 0 }}>
          {send.error instanceof ApiError && send.error.status === 409
            ? 'A conversa foi bloqueada para novos envios.'
            : send.error instanceof ApiError && send.error.isTransient
              ? 'Não foi possível confirmar o envio. Tente novamente; a mensagem não será duplicada.'
              : describeError(send.error).title}
        </p>
      )}
    </form>
  );
}

function Content({ session, perspective }: { session: Session; perspective: Perspective }) {
  const { conversationId = '' } = useParams();
  const conversation = useConversation(conversationId);
  const history = useMessageHistory(conversationId);
  const logRef = useRef<HTMLDivElement>(null);
  const listPath = perspective === 'artist' ? '/estudio/mensagens' : '/mensagens';
  const count = history.messages.length;

  useEffect(() => {
    const log = logRef.current;
    if (log) log.scrollTop = log.scrollHeight;
  }, [count]);

  const back = (
    <Link className="text-button" to={listPath}>
      <ArrowLeft size={16} aria-hidden="true" /> Voltar para conversas
    </Link>
  );

  if (conversation.isPending) return <Loading label="Abrindo conversa…" />;
  if (conversation.isError) {
    return (
      <>
        {back}
        <ErrorState error={conversation.error} onRetry={() => void conversation.refetch()}>
          <Link className="button outline" to={listPath}>
            Ver minhas conversas
          </Link>
        </ErrorState>
      </>
    );
  }

  const data = conversation.data;
  if (!belongsTo(data, perspective, session.profile.id)) {
    const other = perspective === 'artist' ? '/mensagens' : '/estudio/mensagens';
    return (
      <>
        {back}
        <EmptyState title="Esta conversa pertence à outra área da sua conta.">
          <Link className="button outline" to={`${other}/${data.id}`}>
            Abrir na área correta
          </Link>
        </EmptyState>
      </>
    );
  }

  const blocked = data.writingBlocked || data.engagementState === 'Closed';
  const status = conversationStatus(data);
  const engagementPath = perspective === 'artist' ? `/estudio/atendimentos/${data.engagementId}` : `/atendimentos/${data.engagementId}`;

  return (
    <>
      {back}
      <div className="conversation-layout" style={{ marginTop: 16 }}>
        <section className="chat-panel" aria-labelledby="chat-title">
          <div className="chat-heading">
            <Avatar className="avatar" name={data.counterpart.displayName} photoUrl={data.counterpart.photoThumbnailUrl} />
            <div className="grow">
              <h1 id="chat-title" style={{ fontSize: 20 }}>
                {data.counterpart.displayName}
              </h1>
              <span className={status.tone}>{status.label}</span>
            </div>
            <Link className="icon-button" to={engagementPath} aria-label="Ver atendimento e proposta">
              <ArrowUpRight size={18} aria-hidden="true" />
            </Link>
          </div>
          <div className="chat-log" ref={logRef} role="log" aria-label="Histórico da conversa" aria-live="polite" tabIndex={0}>
            {history.status === 'loading' ? (
              <Loading label="Carregando mensagens…" />
            ) : history.status === 'error' && count === 0 ? (
              <ErrorState error={history.error} onRetry={history.refresh} />
            ) : count === 0 ? (
              <p className="muted">Nenhuma mensagem ainda. Comece a conversa!</p>
            ) : (
              history.messages.map((message) => {
                const mine = message.authorId === session.profile.id;
                return (
                  <div key={message.id} className={mine ? 'bubble mine' : 'bubble'}>
                    <span className="visually-hidden">{mine ? 'Você' : data.counterpart.displayName} escreveu:</span>
                    <p>{message.body}</p>
                    <small>
                      {mine ? 'Você' : data.counterpart.displayName} · {dateTime(message.createdAt)} {mine && <Check size={12} aria-hidden="true" />}
                    </small>
                  </div>
                );
              })
            )}
          </div>
          {history.status === 'error' && count > 0 && (
            <p className="notice warning" role="status" style={{ margin: '0 12px' }}>
              Não foi possível atualizar o histórico agora. Tentaremos novamente.
            </p>
          )}
          {blocked ? (
            <div className="chat-closed" role="status">
              <strong>{data.engagementState === 'Closed' ? 'Serviço encerrado' : 'Envio bloqueado'}</strong>
              <span>
                O histórico continua disponível para consulta.{' '}
                {perspective === 'client'
                  ? 'Para conversar novamente, envie um novo pedido ao artista; a nova conversa é liberada quando ele confirmar a reserva.'
                  : 'Uma nova conversa é liberada quando você confirmar a reserva de um novo atendimento com este cliente.'}
              </span>
            </div>
          ) : null}
          <Composer conversationId={data.id} blocked={blocked} onSent={history.refresh} />
        </section>
        <aside className="side-panel">
          <span className="eyebrow">ATENDIMENTO</span>
          <h2>Proposta e agenda</h2>
          <p className="muted">Consulte a proposta, as sessões e o encerramento deste atendimento.</p>
          <Link className="button outline" to={engagementPath}>
            Ver atendimento
          </Link>
        </aside>
      </div>
    </>
  );
}

export function ConversationPage({ perspective }: { perspective: Perspective }) {
  return perspective === 'artist' ? (
    <RequireArtist>{(session) => <Content session={session} perspective="artist" />}</RequireArtist>
  ) : (
    <RequireSession message="Entre para ver esta conversa.">{(session) => <Content session={session} perspective="client" />}</RequireSession>
  );
}
