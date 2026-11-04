import { AlertTriangle, Inbox, Loader2, LogIn } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { ApiError } from '@/shared/api/http';

export function Loading({ label = 'Carregando…' }: { label?: string }) {
  return (
    <div className="state state-loading" role="status" aria-live="polite">
      <Loader2 className="spin" size={22} aria-hidden="true" />
      <span>{label}</span>
    </div>
  );
}

export function describeError(error: unknown): { title: string; detail: string } {
  if (error instanceof ApiError) {
    if (error.status === 0) return { title: 'Sem conexão com o InkLink.', detail: 'Verifique sua internet e tente novamente.' };
    if (error.status === 401) return { title: 'Sua sessão expirou.', detail: 'Entre novamente para continuar.' };
    if (error.status === 403) return { title: 'Acesso não permitido.', detail: 'Sua conta não tem permissão para esta área.' };
    if (error.status === 404) return { title: 'Não encontramos este conteúdo.', detail: 'Ele não existe ou não está disponível para sua conta.' };
    if (error.status === 503) return { title: 'Serviço temporariamente indisponível.', detail: 'Os dados não puderam ser obtidos agora. Nada foi exibido como vazio.' };
    if (error.status === 409) return { title: 'A operação conflita com o estado atual.', detail: 'Atualize a página para ver a situação mais recente.' };
    return { title: 'Algo deu errado.', detail: 'Tente novamente em instantes.' };
  }
  return { title: 'Algo deu errado.', detail: 'Tente novamente em instantes.' };
}

export function ErrorState({ error, onRetry, children }: { error: unknown; onRetry?: () => void; children?: ReactNode }) {
  const { title, detail } = describeError(error);
  const traceId = error instanceof ApiError ? error.traceId : undefined;
  return (
    <div className="state state-error" role="alert">
      <AlertTriangle size={24} aria-hidden="true" />
      <strong>{title}</strong>
      <p>{detail}</p>
      {traceId && <small>Código para suporte: {traceId}</small>}
      <div className="state-actions">
        {onRetry && (
          <button type="button" className="button outline" onClick={onRetry}>
            Tentar novamente
          </button>
        )}
        {children}
      </div>
    </div>
  );
}

export function EmptyState({ title, children, icon }: { title: string; children?: ReactNode; icon?: ReactNode }) {
  return (
    <div className="state state-empty">
      {icon ?? <Inbox size={26} aria-hidden="true" />}
      <strong>{title}</strong>
      {children}
    </div>
  );
}

/** Convite das áreas privadas: leva à página de login preservando o destino (decisão 0017). */
export function SignInPrompt({ href, message }: { href: string; message: string }) {
  return (
    <div className="state state-empty">
      <LogIn size={26} aria-hidden="true" />
      <strong>{message}</strong>
      <Link className="button red" to={href}>
        Entrar
      </Link>
    </div>
  );
}

/** Mensagem de validação por campo a partir do Problem Details. */
export function fieldError(error: unknown, ...fields: string[]): string | undefined {
  if (!(error instanceof ApiError)) return undefined;
  for (const [key, messages] of Object.entries(error.fieldErrors)) {
    if (fields.some((field) => key.toLowerCase() === field.toLowerCase())) return messages[0];
  }
  return undefined;
}
