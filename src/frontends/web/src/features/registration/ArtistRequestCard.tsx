import { Brush, Hourglass } from 'lucide-react';
import { registrationMessage, useRegistration, useRequestArtist } from './api';

/** Solicitação da função Artist (RF22): fica pendente até a aprovação administrativa (decisão 0020). */
export function ArtistRequestCard() {
  const registration = useRegistration();
  const request = useRequestArtist();
  const status = registration.data?.artistRequest;
  if (!registration.data || status === 'Approved') return null;
  return (
    <section className="form-card artist-request" aria-labelledby="artist-request-title">
      <div className="grow">
        <span className="eyebrow">ATUE COMO ARTISTA</span>
        <h2 id="artist-request-title">{status === 'Pending' ? 'Solicitação em análise' : 'Você é tatuador?'}</h2>
        <p className="muted">
          {status === 'Pending'
            ? 'Recebemos sua solicitação. O Estúdio do artista é liberado depois da aprovação da equipe do InkLink.'
            : 'Solicite a função de artista para publicar seu portfólio, receber pedidos e organizar sua agenda.'}
        </p>
        {request.error && (
          <p className="notice error" role="alert">
            {registrationMessage(request.error)}
          </p>
        )}
      </div>
      {status === 'Pending' ? (
        <span className="status amber">
          <Hourglass size={14} aria-hidden="true" /> Em análise
        </span>
      ) : (
        <button type="button" className="button outline" onClick={() => request.mutate()} disabled={request.isPending}>
          <Brush size={16} aria-hidden="true" /> {request.isPending ? 'Enviando…' : 'Quero ser artista'}
        </button>
      )}
    </section>
  );
}
