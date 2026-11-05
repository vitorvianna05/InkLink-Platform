import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { ApiError, request } from '@/shared/api/http';
import { Avatar } from '@/shared/ui/Avatar';
import { Dialog } from '@/shared/ui/Dialog';
import { describeError } from '@/shared/ui/States';
import type { Session } from './session';

// Decisão 0018: JPEG, PNG ou WebP até 5 MB; o backend confere o conteúdo e recorta em quadrado.
const accepted = ['image/jpeg', 'image/png', 'image/webp'];
const maxBytes = 5 * 1024 * 1024;

/** Foto de perfil (T10.18): uma por conta, enviada por upload. */
export function ProfilePhotoDialog({ session, open, onClose }: { session: Session; open: boolean; onClose: () => void }) {
  const client = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const refresh = () => client.invalidateQueries({ queryKey: ['session'] });
  const upload = useMutation({
    mutationFn: (image: File) => {
      const form = new FormData();
      form.set('image', image, image.name);
      return request('/api/v1/profiles/me/photo', { method: 'PUT', body: form });
    },
    onSuccess: async () => {
      await refresh();
      onClose();
    },
  });
  const remove = useMutation({
    mutationFn: () => request('/api/v1/profiles/me/photo', { method: 'DELETE' }),
    onSuccess: async () => {
      await refresh();
      onClose();
    },
  });

  useEffect(() => () => {
    if (preview) URL.revokeObjectURL(preview);
  }, [preview]);

  const choose = (next: File | null) => {
    setError(null);
    if (preview) URL.revokeObjectURL(preview);
    setPreview(null);
    setFile(null);
    if (!next) return;
    if (!accepted.includes(next.type)) return setError('Use uma imagem JPEG, PNG ou WebP.');
    if (next.size > maxBytes) return setError('A imagem deve ter no máximo 5 MB.');
    setFile(next);
    setPreview(URL.createObjectURL(next));
  };

  const failure = upload.error ?? remove.error;
  const busy = upload.isPending || remove.isPending;
  const current = session.profile.photoUrl ?? null;

  return (
    <Dialog open={open} onClose={onClose} title="Foto de perfil" eyebrow="SUA CONTA">
      <div className="stack">
        <div className="photo-preview">
          {preview ? <img src={preview} alt="Prévia da nova foto" /> : <Avatar className="avatar large" name={session.profile.displayName} photoUrl={current} />}
        </div>
        <p className="muted">
          {session.isArtist
            ? 'Sua foto aparece no seu perfil público de artista e nas conversas.'
            : 'Sua foto fica visível apenas para os artistas com quem você tem atendimento.'}
        </p>
        <div className="field">
          <label htmlFor="profile-photo-file">Escolher imagem</label>
          <input id="profile-photo-file" type="file" accept={accepted.join(',')} onChange={(event) => choose(event.target.files?.[0] ?? null)} />
          <small className="field-hint">JPEG, PNG ou WebP até 5 MB, com pelo menos 256 px. A imagem é recortada em formato quadrado.</small>
        </div>
        {(error || failure) && (
          <p className="notice error" role="alert">
            {error ?? (failure instanceof ApiError && Object.values(failure.fieldErrors)[0]?.[0]) ?? describeError(failure).title}
          </p>
        )}
        <button type="button" className="button red full" disabled={busy || !file} onClick={() => file && upload.mutate(file)}>
          {upload.isPending ? 'Enviando…' : 'Salvar foto'}
        </button>
        {current && (
          <button type="button" className="button outline full" disabled={busy} onClick={() => remove.mutate()}>
            {remove.isPending ? 'Removendo…' : 'Remover foto'}
          </button>
        )}
      </div>
    </Dialog>
  );
}
