import { Send } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import type { Artwork } from '@/shared/api/contracts';
import { ApiError, newIdempotencyKey } from '@/shared/api/http';
import { money } from '@/shared/lib/format';
import { Dialog } from '@/shared/ui/Dialog';
import { Field } from '@/shared/ui/Field';
import { describeError, fieldError } from '@/shared/ui/States';
import { useRequestEngagement } from './api';

interface Props {
  open: boolean;
  onClose: () => void;
  artistId: string;
  artistName: string;
  flash?: Artwork | null;
}

interface Errors {
  idea?: string;
  bodyRegion?: string;
  size?: string;
}

/** Validação de interface alinhada à decisão 0009; o backend continua validando tudo. */
export function validateRequest(idea: string, bodyRegion: string, size: string): Errors {
  const errors: Errors = {};
  const ideaText = idea.trim();
  if (!ideaText) errors.idea = 'Descreva sua ideia.';
  else if (ideaText.length > 4000) errors.idea = 'Use no máximo 4.000 caracteres.';
  const region = bodyRegion.trim();
  if (!region) errors.bodyRegion = 'Informe a região do corpo.';
  else if (region.length > 120) errors.bodyRegion = 'Use no máximo 120 caracteres.';
  const value = Number(size.replace(',', '.'));
  if (!size.trim() || !Number.isFinite(value) || value <= 0) errors.size = 'Informe um tamanho maior que zero.';
  else if (value > 300) errors.size = 'O tamanho máximo é 300 cm.';
  else if (!/^\d+([.,]\d{1,2})?$/.test(size.trim())) errors.size = 'Use até duas casas decimais.';
  return errors;
}

export function RequestDialog({ open, onClose, artistId, artistName, flash }: Props) {
  const navigate = useNavigate();
  const mutation = useRequestEngagement();
  const [idea, setIdea] = useState('');
  const [bodyRegion, setBodyRegion] = useState('');
  const [size, setSize] = useState('');
  const [errors, setErrors] = useState<Errors>({});
  // Uma chave por intenção: repetir após falha transitória recupera o mesmo pedido.
  const [key, setKey] = useState(newIdempotencyKey);

  function close() {
    if (mutation.isPending) return;
    mutation.reset();
    onClose();
  }

  const serverError = mutation.error;
  const ideaError = errors.idea ?? fieldError(serverError, 'idea');
  const regionError = errors.bodyRegion ?? fieldError(serverError, 'bodyRegion');
  const sizeError = errors.size ?? fieldError(serverError, 'approximateSizeCm');

  return (
    <Dialog open={open} onClose={close} title="Conta sua ideia." eyebrow="O PRIMEIRO TRAÇO">
      <p>
        Envie sua inspiração para <strong>{artistName}</strong>. O envio abre a negociação; não há cobrança, aceite ou reserva de horário.
      </p>
      {flash && (
        <p className="notice">
          A partir do flash <strong>{flash.title}</strong> ({flash.style}
          {flash.referencePrice !== null ? ` · referência ${money(flash.referencePrice)}` : ''}).
        </p>
      )}
      <form
        className="stack"
        noValidate
        onSubmit={(event) => {
          event.preventDefault();
          const found = validateRequest(idea, bodyRegion, size);
          setErrors(found);
          if (Object.keys(found).length) return;
          mutation.mutate(
            {
              key,
              input: {
                artistId,
                idea: idea.trim(),
                bodyRegion: bodyRegion.trim(),
                approximateSizeCm: Number(size.replace(',', '.')),
                flashId: flash?.id ?? null,
              },
            },
            {
              onSuccess: (engagement) => {
                setKey(newIdempotencyKey());
                onClose();
                navigate(`/atendimentos/${engagement.id}`, { state: { created: true } });
              },
              onError: (error) => {
                // Conflito ou validação exigem nova intenção; falhas transitórias reaproveitam a chave.
                if (error instanceof ApiError && !error.isTransient && !error.isSessionExpired) setKey(newIdempotencyKey());
              },
            },
          );
        }}
      >
        <Field label="Sua ideia" error={ideaError}>
          {(props) => (
            <textarea {...props} value={idea} maxLength={4000} onChange={(e) => setIdea(e.target.value)} placeholder="Uma composição botânica, com folhas e flores…" data-autofocus />
          )}
        </Field>
        <div className="form-grid">
          <Field label="Região do corpo" error={regionError}>
            {(props) => <input {...props} value={bodyRegion} maxLength={120} onChange={(e) => setBodyRegion(e.target.value)} placeholder="Antebraço" />}
          </Field>
          <Field label="Tamanho aproximado (cm)" error={sizeError}>
            {(props) => <input {...props} value={size} inputMode="decimal" onChange={(e) => setSize(e.target.value)} placeholder="12" />}
          </Field>
        </div>
        {serverError && !fieldError(serverError, 'idea', 'bodyRegion', 'approximateSizeCm') && (
          <p className="notice error" role="alert">
            {describeError(serverError).title}{' '}
            {serverError instanceof ApiError && serverError.isTransient
              ? 'Tente enviar novamente: se o pedido já tiver sido registrado, ele será recuperado sem duplicar.'
              : describeError(serverError).detail}
          </p>
        )}
        <button type="submit" className="button red full" disabled={mutation.isPending}>
          {mutation.isPending ? 'Enviando…' : 'Enviar minha ideia'} <Send size={16} aria-hidden="true" />
        </button>
      </form>
    </Dialog>
  );
}
