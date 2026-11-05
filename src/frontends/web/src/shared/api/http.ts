// Cliente HTTP único do website. Todas as chamadas passam pelo BFF na mesma origem:
// o navegador envia apenas o cookie de sessão e nunca recebe tokens.

export interface ProblemDetails {
  status: number;
  code: string;
  title?: string;
  detail?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

/** Falha HTTP já interpretada a partir de application/problem+json. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly traceId?: string;
  readonly fieldErrors: Record<string, string[]>;

  constructor(problem: ProblemDetails) {
    super(problem.title ?? problem.code);
    this.name = 'ApiError';
    this.status = problem.status;
    this.code = problem.code;
    this.traceId = problem.traceId;
    this.fieldErrors = problem.errors ?? {};
  }

  /** Sessão ausente ou expirada no BFF. */
  get isSessionExpired(): boolean {
    return this.status === 401;
  }

  /** Falha de rede, timeout ou indisponibilidade: a escrita pode ter ocorrido. */
  get isTransient(): boolean {
    return this.status === 0 || this.status === 502 || this.status === 503 || this.status === 504;
  }
}

type SessionListener = () => void;
const sessionListeners = new Set<SessionListener>();

/** Notifica a camada de sessão quando qualquer chamada recebe 401. */
export function onSessionExpired(listener: SessionListener): () => void {
  sessionListeners.add(listener);
  return () => sessionListeners.delete(listener);
}

let csrfToken: Promise<string> | null = null;

async function readCsrfToken(): Promise<string> {
  csrfToken ??= (async () => {
    let response: Response;
    try {
      response = await fetch('/bff/antiforgery', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
    } catch {
      throw new ApiError({ status: 0, code: 'network.unavailable' });
    }
    if (!response.ok) throw await toApiError(response);
    const body = (await response.json()) as { requestToken: string };
    return body.requestToken;
  })();
  try {
    return await csrfToken;
  } catch (error) {
    csrfToken = null;
    throw error;
  }
}

/** Descarta o token antiforgery após login, logout ou rejeição. */
export function resetCsrfToken(): void {
  csrfToken = null;
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: Partial<ProblemDetails> = {};
  const type = response.headers.get('Content-Type') ?? '';
  if (type.includes('json')) {
    try {
      problem = (await response.json()) as Partial<ProblemDetails>;
    } catch {
      problem = {};
    }
  }
  return new ApiError({
    status: response.status,
    code: problem.code ?? `http.${response.status}`,
    title: problem.title,
    detail: problem.detail,
    traceId: problem.traceId ?? response.headers.get('X-Trace-Id') ?? undefined,
    errors: problem.errors,
  });
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  /** UUID estável por intenção do usuário; repetido em novas tentativas da mesma ação. */
  idempotencyKey?: string;
  signal?: AbortSignal;
  query?: Record<string, string | number | boolean | null | undefined>;
}

export interface ApiResponse<T> {
  status: number;
  data: T;
}

function withQuery(path: string, query?: RequestOptions['query']): string {
  if (!query) return path;
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue;
    params.set(key, String(value));
  }
  const text = params.toString();
  return text ? `${path}?${text}` : path;
}

async function send(path: string, options: RequestOptions, retryCsrf: boolean): Promise<Response> {
  const method = options.method ?? 'GET';
  const headers: Record<string, string> = { Accept: 'application/json' };
  // FormData (upload de imagens) define o próprio Content-Type com o boundary do multipart.
  const multipart = options.body instanceof FormData;
  if (options.body !== undefined && !multipart) headers['Content-Type'] = 'application/json';
  if (options.idempotencyKey) headers['Idempotency-Key'] = options.idempotencyKey;
  if (method !== 'GET') headers['X-CSRF-TOKEN'] = await readCsrfToken();

  let response: Response;
  try {
    response = await fetch(withQuery(path, options.query), {
      method,
      headers,
      credentials: 'same-origin',
      body: options.body === undefined ? undefined : multipart ? (options.body as FormData) : JSON.stringify(options.body),
      signal: options.signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new ApiError({ status: 0, code: 'network.unavailable' });
  }

  if (response.status === 400 && method !== 'GET' && retryCsrf) {
    const error = await toApiError(response.clone());
    if (error.code === 'csrf.invalid') {
      resetCsrfToken();
      return send(path, options, false);
    }
  }
  return response;
}

/** Executa a chamada e devolve status + corpo JSON (ou undefined em 202/204 sem corpo). */
export async function request<T>(path: string, options: RequestOptions = {}): Promise<ApiResponse<T>> {
  let response: Response;
  try {
    response = await send(path, options, true);
    if (!response.ok) throw await toApiError(response);
  } catch (error) {
    if (error instanceof ApiError && error.isSessionExpired) {
      resetCsrfToken();
      sessionListeners.forEach((listener) => listener());
    }
    throw error;
  }
  const text = response.status === 204 ? '' : await response.text();
  return { status: response.status, data: (text ? JSON.parse(text) : undefined) as T };
}

export async function getJson<T>(path: string, options: Omit<RequestOptions, 'method' | 'body'> = {}): Promise<T> {
  return (await request<T>(path, options)).data;
}

export function newIdempotencyKey(): string {
  return crypto.randomUUID();
}
