import { useState } from 'react';
import type { Engagement } from '@/shared/api/contracts';
import { ApiError } from '@/shared/api/http';
import { dateTime } from '@/shared/lib/format';
import { Dialog } from '@/shared/ui/Dialog';
import { describeError, ErrorState, Loading } from '@/shared/ui/States';
import { useCloseEngagement, useLifecycle, useRetryLifecycle } from './api';

const statusLabels: Record<string, string> = {
  Pending: 'Em processamento',
  Completed: 'Concluída',
  Rejected: 'Recusada',
  Quarantined: 'Interrompida — requer nova tentativa',
};

const kindLabels: Record<string, string> = { Close: 'Encerramento', Activate: 'Abertura da nova conversa' };

const errorLabels: Record<string, string> = {
  'scheduling.future_reservations': 'Há sessões com término futuro. O encerramento só é possível depois delas.',
  'engagements.future_reservations': 'Há sessões com término futuro. O encerramento só é possível depois delas.',
};

/**
 * Encerramento e novo atendimento (P07): distingue processo pendente de encerramento
 * definitivo, mostra recusas e permite retomar operações interrompidas.
 */
export function LifecycleSection({ engagement, perspective }: { engagement: Engagement; perspective: 'client' | 'artist' }) {
  const lifecycle = useLifecycle(engagement.id);
  const close = useCloseEngagement(engagement.id);
  const retry = useRetryLifecycle(engagement.id);
  const [confirming, setConfirming] = useState(false);

  if (lifecycle.isPending) return <Loading label="Consultando a situação do atendimento…" />;
  if (lifecycle.isError) return <ErrorState error={lifecycle.error} onRetry={() => void lifecycle.refetch()} />;

  const status = lifecycle.data;
  const closing = status.state === 'Closing';
  const closed = status.state === 'Closed';
  const quarantined = status.operations.some((op) => op.status === 'Quarantined');
  const pendingActivation = status.operations.some((op) => op.kind === 'Activate' && op.status === 'Pending');
  const lastClose = [...status.operations].reverse().find((op) => op.kind === 'Close');

  return (
    <section className="side-panel" aria-labelledby="lifecycle-title">
      <span className="eyebrow">CICLO DO ATENDIMENTO</span>
      <h2 id="lifecycle-title">
        {closed ? 'Serviço encerrado' : closing ? 'Encerramento em andamento' : 'Atendimento ativo'}
      </h2>
      {closed && <p>O histórico continua disponível para consulta. Novas mensagens estão bloqueadas para as duas partes.</p>}
      {closing && (
        <p className="notice warning" role="status">
          O encerramento foi solicitado e está sendo confirmado com a agenda e as conversas. Ele ainda não é definitivo.
        </p>
      )}
      {lastClose?.status === 'Rejected' && !closing && !closed && (
        <p className="notice warning">
          O último pedido de encerramento foi recusado: {errorLabels[lastClose.error ?? ''] ?? 'o atendimento ainda possui pendências.'}
        </p>
      )}
      {status.requiresReservation && !closed && (
        <p className="notice">
          Este é um novo atendimento com {perspective === 'client' ? 'este artista' : 'este cliente'} após um serviço encerrado. A conversa só é liberada depois que{' '}
          {perspective === 'client' ? 'o artista confirmar' : 'você confirmar'} uma nova reserva na agenda.
        </p>
      )}
      {pendingActivation && (
        <p className="notice warning" role="status">
          A reserva foi confirmada e a liberação da nova conversa está em processamento.
        </p>
      )}
      {status.operations.length > 0 && (
        <ul className="list" aria-label="Operações do ciclo">
          {status.operations.map((op) => (
            <li key={op.id} className="list-item">
              <div className="grow">
                <strong>{kindLabels[op.kind] ?? op.kind}</strong>
                <p>
                  {statusLabels[op.status] ?? op.status} · atualizado em {dateTime(op.updatedAt)}
                  {op.attempts > 1 && ` · ${op.attempts} tentativas`}
                </p>
              </div>
            </li>
          ))}
        </ul>
      )}
      {quarantined && (
        <button type="button" className="button outline" disabled={retry.isPending} onClick={() => retry.mutate()}>
          {retry.isPending ? 'Retomando…' : 'Tentar novamente'}
        </button>
      )}
      {retry.isError && <p className="notice error">{describeError(retry.error).title}</p>}
      {!closed && !closing && (
        <button type="button" className="button outline" onClick={() => setConfirming(true)}>
          Encerrar serviço
        </button>
      )}
      {close.isError && (
        <p className="notice error" role="alert">
          {close.error instanceof ApiError && close.error.status === 409
            ? 'Não foi possível encerrar agora. Verifique se há sessões futuras agendadas.'
            : `${describeError(close.error).title} ${describeError(close.error).detail}`}
        </p>
      )}

      <Dialog open={confirming} onClose={() => setConfirming(false)} title="Encerrar este serviço?" eyebrow="CONCLUIR ATENDIMENTO">
        <p>
          As mensagens continuarão armazenadas para consulta, mas novos envios serão bloqueados para as duas partes. Para conversar novamente, será necessário um novo pedido e
          uma nova reserva confirmada pelo artista.
        </p>
        <p className="muted">O encerramento não depende de aprovação da outra parte e é recusado se houver sessões com término futuro.</p>
        <button
          type="button"
          className="button red full"
          disabled={close.isPending}
          onClick={() =>
            close.mutate(undefined, {
              onSettled: () => {
                setConfirming(false);
                void lifecycle.refetch();
              },
            })
          }
        >
          {close.isPending ? 'Solicitando…' : 'Confirmar encerramento'}
        </button>
      </Dialog>
    </section>
  );
}
