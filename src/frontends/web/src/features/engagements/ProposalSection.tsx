import { Check } from 'lucide-react';
import { useState } from 'react';
import type { Engagement } from '@/shared/api/contracts';
import { ApiError, newIdempotencyKey } from '@/shared/api/http';
import { dateTime, money, proposalStateLabel } from '@/shared/lib/format';
import { Dialog } from '@/shared/ui/Dialog';
import { Field } from '@/shared/ui/Field';
import { describeError, ErrorState, fieldError, Loading } from '@/shared/ui/States';
import { useAcceptProposal, usePresentProposal, useProposals, type Perspective } from './api';

export function parseAmount(text: string): number | null {
  const normalized = text.trim().replace(/\./g, '').replace(',', '.');
  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) return null;
  const value = Number(normalized);
  return value > 0 && value <= 9_999_999.99 ? value : null;
}

function PresentForm({ engagement }: { engagement: Engagement }) {
  const mutation = usePresentProposal(engagement.id);
  const [description, setDescription] = useState('');
  const [amount, setAmount] = useState('');
  const [errors, setErrors] = useState<{ description?: string; amount?: string }>({});
  const [key, setKey] = useState(newIdempotencyKey);
  const replacing = engagement.currentProposal?.state === 'Pending';
  const descriptionError = errors.description ?? fieldError(mutation.error, 'description');
  const amountError = errors.amount ?? fieldError(mutation.error, 'amount');

  return (
    <form
      className="form-card"
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        const found: typeof errors = {};
        const text = description.trim();
        if (!text) found.description = 'Descreva a proposta.';
        else if (text.length > 4000) found.description = 'Use no máximo 4.000 caracteres.';
        const value = parseAmount(amount);
        if (value === null) found.amount = 'Informe um valor maior que zero, até R$ 9.999.999,99, com até duas casas decimais.';
        setErrors(found);
        if (Object.keys(found).length || value === null) return;
        mutation.mutate(
          { description: text, amount: value, key },
          {
            onSuccess: () => {
              setDescription('');
              setAmount('');
              setKey(newIdempotencyKey());
            },
            onError: (error) => {
              if (error instanceof ApiError && !error.isTransient && !error.isSessionExpired) setKey(newIdempotencyKey());
            },
          },
        );
      }}
    >
      <h3>{replacing ? 'Substituir a proposta vigente' : 'Apresentar proposta'}</h3>
      {replacing && <p className="muted">A nova versão substitui a atual enquanto ela não for aceita. As versões anteriores continuam consultáveis.</p>}
      <Field label="Descrição" error={descriptionError}>
        {(props) => <textarea {...props} value={description} maxLength={4000} onChange={(e) => setDescription(e.target.value)} />}
      </Field>
      <Field label="Valor (R$)" error={amountError}>
        {(props) => <input {...props} value={amount} inputMode="decimal" placeholder="450,00" onChange={(e) => setAmount(e.target.value)} />}
      </Field>
      {mutation.isError && !descriptionError && !amountError && (
        <p className="notice error" role="alert">
          {describeError(mutation.error).title} {describeError(mutation.error).detail}
        </p>
      )}
      {mutation.isSuccess && (
        <p className="notice success" role="status">
          Proposta enviada ao cliente.
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button red" disabled={mutation.isPending}>
          {mutation.isPending ? 'Enviando…' : 'Enviar proposta'}
        </button>
      </div>
    </form>
  );
}

export function ProposalSection({ engagement, perspective }: { engagement: Engagement; perspective: Perspective }) {
  const proposals = useProposals(engagement.id);
  const accept = useAcceptProposal(engagement.id);
  const [confirming, setConfirming] = useState(false);
  const current = engagement.currentProposal;
  const canAccept = perspective === 'client' && engagement.state === 'Negotiating' && current?.state === 'Pending';

  return (
    <section className="side-panel" aria-labelledby="proposal-title">
      <span className="eyebrow">FEITO PARA VOCÊ</span>
      <h2 id="proposal-title">{current ? 'Proposta vigente' : 'Proposta'}</h2>
      {current ? (
        <>
          <p>{current.description}</p>
          <span className="price">{money(current.amount)}</span>
          <span className={current.state === 'Accepted' ? 'status green' : 'status'}>{proposalStateLabel(current.state)}</span>
          <small className="muted">O aceite não realiza cobrança nem reserva horário. A data é combinada na conversa e confirmada pelo artista na agenda.</small>
        </>
      ) : (
        <p className="muted">{perspective === 'client' ? 'O artista ainda não apresentou uma proposta.' : 'Apresente uma proposta para este pedido.'}</p>
      )}
      {canAccept && (
        <button type="button" className="button red" onClick={() => setConfirming(true)}>
          Aceitar proposta
        </button>
      )}
      {current?.state === 'Accepted' && (
        <span className="row">
          <Check size={16} aria-hidden="true" /> Aceita em {dateTime(current.acceptedAt ?? current.createdAt)}
        </span>
      )}
      {accept.isError && (
        <p className="notice error" role="alert">
          {accept.error instanceof ApiError && accept.error.status === 409
            ? 'Esta proposta foi substituída ou o atendimento mudou. A página foi atualizada com a situação atual.'
            : `${describeError(accept.error).title} ${describeError(accept.error).detail}`}
        </p>
      )}

      {perspective === 'artist' && engagement.state === 'Negotiating' && <PresentForm engagement={engagement} />}

      <details>
        <summary>Histórico de propostas</summary>
        {proposals.isPending ? (
          <Loading />
        ) : proposals.isLoadingError ? (
          <ErrorState error={proposals.error} onRetry={() => void proposals.refetch()} />
        ) : proposals.data.items.length === 0 ? (
          <p className="muted">Nenhuma proposta registrada.</p>
        ) : (
          <ul className="list">
            {proposals.data.items.map((proposal) => (
              <li key={proposal.id} className="list-item">
                <div className="grow">
                  <strong>{money(proposal.amount)}</strong>
                  <p>{proposal.description}</p>
                  <small className="muted">{dateTime(proposal.createdAt)}</small>
                </div>
                <span className="status">{proposalStateLabel(proposal.state)}</span>
              </li>
            ))}
          </ul>
        )}
      </details>

      <Dialog open={confirming} onClose={() => setConfirming(false)} title="Confirmar o aceite?" eyebrow="O PRÓXIMO CAPÍTULO É SEU">
        {current && (
          <>
            <p>
              Você está aceitando a proposta de <strong>{engagement.catalog.name}</strong> no valor de <strong>{money(current.amount)}</strong>.
            </p>
            <p className="muted">Nenhuma cobrança é feita. Depois do aceite, combine a data da sessão pela conversa.</p>
            <button
              type="button"
              className="button red full"
              disabled={accept.isPending}
              onClick={() => accept.mutate(current.id, { onSettled: () => setConfirming(false) })}
            >
              {accept.isPending ? 'Confirmando…' : 'Confirmar aceite'}
            </button>
          </>
        )}
      </Dialog>
    </section>
  );
}
