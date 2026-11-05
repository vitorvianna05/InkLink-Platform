import { useQueryClient, type Query } from '@tanstack/react-query';
import { RefreshCw } from 'lucide-react';
import { useCallback, useSyncExternalStore } from 'react';
import { ApiError } from '@/shared/api/http';

/**
 * Consulta exibida na tela cuja última atualização falhou, mas que ainda tem dados carregados.
 * As páginas mantêm esses dados (e os formulários abertos); este aviso deixa a defasagem explícita
 * (spec §5.8 e arquitetura §6.3). Sessão expirada fica de fora: o aviso de novo login já a explica.
 */
function isStale(query: Query): boolean {
  const { status, data, error } = query.state;
  if (status !== 'error' || data === undefined || query.getObserversCount() === 0) return false;
  if (query.queryKey[0] === 'session') return false;
  return !(error instanceof ApiError && error.isSessionExpired);
}

export function StaleDataNotice() {
  const client = useQueryClient();
  const cache = client.getQueryCache();
  // O instantâneo é uma string para manter a referência estável entre leituras.
  const snapshot = useCallback(() => cache.getAll().filter(isStale).map((query) => query.queryHash).join('|'), [cache]);
  const stale = useSyncExternalStore(useCallback((notify) => cache.subscribe(notify), [cache]), snapshot, snapshot);
  if (!stale) return null;
  const hashes = new Set(stale.split('|'));
  return (
    <p className="notice warning stale-notice" role="status">
      <span>Não foi possível atualizar alguns dados agora. O que está na tela pode estar desatualizado.</span>
      <button type="button" className="button outline" onClick={() => void client.refetchQueries({ predicate: (query) => hashes.has(query.queryHash) })}>
        <RefreshCw size={14} aria-hidden="true" /> Atualizar agora
      </button>
    </p>
  );
}
