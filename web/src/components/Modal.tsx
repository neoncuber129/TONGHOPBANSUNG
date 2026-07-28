import { type ReactNode } from 'react'

export function Modal({
  title,
  children,
  footer,
  onClose,
  wide,
}: {
  title: string
  children: ReactNode
  footer?: ReactNode
  onClose: () => void
  wide?: boolean
}) {
  return (
    <div className="modal-backdrop" onClick={onClose} role="presentation">
      <div
        className={`modal ${wide ? 'modal-wide' : ''} ${footer ? 'modal-with-footer' : ''}`}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal
      >
        <header className="modal-header">
          <h2>{title}</h2>
          <button type="button" className="icon-btn" onClick={onClose} aria-label="Đóng">
            ×
          </button>
        </header>
        <div className="modal-body">{children}</div>
        {footer ? <footer className="modal-footer">{footer}</footer> : null}
      </div>
    </div>
  )
}

export function BusyOverlay({ message }: { message: string }) {
  return (
    <div className="busy-overlay">
      <div className="busy-card">
        <div className="spinner" />
        <p>{message}</p>
      </div>
    </div>
  )
}
