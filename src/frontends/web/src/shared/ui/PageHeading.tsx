import type { ReactNode } from 'react';

export function PageHeading({ eyebrow, title, children }: { eyebrow: string; title: ReactNode; children?: ReactNode }) {
  return (
    <div className="page-heading">
      <span className="eyebrow">{eyebrow}</span>
      <h1>{title}</h1>
      {children && <p>{children}</p>}
    </div>
  );
}

export function SectionHeading({ eyebrow, title, count, children }: { eyebrow?: string; title: string; count?: number; children?: ReactNode }) {
  return (
    <div className="section-heading">
      <div>
        {eyebrow && <span className="eyebrow">{eyebrow}</span>}
        <h2>
          {title} {count !== undefined && <span className="count">{count}</span>}
        </h2>
      </div>
      {children}
    </div>
  );
}
