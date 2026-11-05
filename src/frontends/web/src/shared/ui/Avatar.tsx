import { useState } from 'react';
import { initials } from '@/shared/lib/format';

/**
 * Avatar com a foto de perfil (decisão 0018) ou as iniciais. Se a imagem falhar — por exemplo, um
 * endereço assinado já expirado —, volta às iniciais sem quebrar o layout.
 */
export function Avatar({ name, photoUrl, className = 'avatar' }: { name: string; photoUrl?: string | null; className?: string }) {
  const [failed, setFailed] = useState<string | null>(null);
  const showPhoto = Boolean(photoUrl) && failed !== photoUrl;
  return (
    <span className={showPhoto ? `${className} with-photo` : className} aria-hidden="true">
      {showPhoto ? <img src={photoUrl!} alt="" loading="lazy" onError={() => setFailed(photoUrl!)} /> : initials(name)}
    </span>
  );
}
