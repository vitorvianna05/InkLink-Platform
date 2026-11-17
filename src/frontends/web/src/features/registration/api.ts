import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError, getJson, request } from '@/shared/api/http';

/** Situação do cadastro pelo próprio usuário (decisão 0020). */
export interface Registration {
  state: 'Pending' | 'Active';
  email: string | null;
  emailConfirmed: boolean;
  phoneNumber: string | null;
  phoneConfirmed: boolean;
  termsVersion: string;
  termsAccepted: boolean;
  artistRequest: 'None' | 'Pending' | 'Approved';
  emailResendAt: string | null;
  phoneResendAt: string | null;
}

export type ContactChannel = 'email' | 'phone';

export const registrationKey = ['registration'] as const;

export function useRegistration(enabled = true) {
  return useQuery({ queryKey: registrationKey, queryFn: ({ signal }) => getJson<Registration>('/api/v1/registration', { signal }), enabled, staleTime: 0 });
}

/** Cada ação devolve a situação atualizada; o cache recebe o resultado sem nova consulta. */
function useRegistrationAction<TInput>(action: (input: TInput) => Promise<Registration>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: action,
    onSuccess: (registration) => client.setQueryData(registrationKey, registration),
    // Limite de envio ou de tentativas: a situação traz o próximo horário de reenvio.
    onError: (error) => {
      if (error instanceof ApiError && error.status === 429) void client.invalidateQueries({ queryKey: registrationKey });
    },
  });
}

const send = async (path: string, method: 'PUT' | 'POST', body?: unknown) => (await request<Registration>(path, { method, body })).data;

export const useAcceptTerms = () => useRegistrationAction((version: string) => send('/api/v1/registration/terms', 'PUT', { version }));
export const useChangePhone = () => useRegistrationAction((phoneNumber: string) => send('/api/v1/registration/phone', 'PUT', { phoneNumber }));
export const useSendCode = () => useRegistrationAction((channel: ContactChannel) => send('/api/v1/registration/codes', 'POST', { channel }));
export const useVerifyCode = () =>
  useRegistrationAction(({ channel, code }: { channel: ContactChannel; code: string }) => send('/api/v1/registration/codes/verify', 'POST', { channel, code }));
export const useRequestArtist = () => useRegistrationAction<void>(() => send('/api/v1/registration/artist-request', 'POST'));

/** Mensagem por código do Problem Details (o texto da resposta não é interpretado). */
export function registrationMessage(error: unknown): string {
  const code = error instanceof ApiError ? error.code : '';
  switch (code) {
    case 'registration.code_invalid':
      return 'Código inválido. Confira os 8 dígitos e tente novamente.';
    case 'registration.code_expired':
      return 'Este código expirou. Peça um novo código.';
    case 'registration.code_attempts_exceeded':
      return 'Tentativas esgotadas para este código. Peça um novo código.';
    case 'registration.resend_too_soon':
      return 'Aguarde alguns segundos antes de pedir outro código.';
    case 'registration.send_limit':
      return 'Limite de envios atingido. Tente novamente mais tarde.';
    case 'registration.phone_invalid':
      return 'Informe um celular válido com DDD, por exemplo (11) 98765-4321.';
    case 'registration.phone_required':
      return 'Informe o celular antes de pedir o código.';
    case 'registration.channel_unavailable':
      return 'Não foi possível enviar o código agora. Tente novamente em instantes.';
    case 'registration.terms_outdated':
      return 'Os termos foram atualizados. Recarregue a página e revise a versão vigente.';
    case 'registration.already_confirmed':
      return 'Este contato já está confirmado.';
    default:
      return 'Não foi possível concluir esta etapa. Tente novamente.';
  }
}
