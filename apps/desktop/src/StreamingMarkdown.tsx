import { memo, useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { invoke } from '@tauri-apps/api/core';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { Icon } from './Icon';

export function toSafeExternalUrl(href?: string): string | null {
  if (!href) return null;
  try {
    const parsed = new URL(href);
    return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? parsed.toString() : null;
  } catch {
    return null;
  }
}

async function openExternalLink(url: string) {
  if ('__TAURI_INTERNALS__' in window) {
    try { await invoke('open_external_url', { url }); }
    catch { /* Host rejection or spawn failure is ignored to keep transcript safe. */ }
    return;
  }
  window.open(url, '_blank', 'noopener,noreferrer');
}

function CodeBlock({ children }: { children?: ReactNode }) {
  const preRef = useRef<HTMLPreElement | null>(null);
  const copyTimer = useRef<number | null>(null);
  const [copied, setCopied] = useState(false);

  useEffect(() => () => {
    if (copyTimer.current !== null) window.clearTimeout(copyTimer.current);
  }, []);

  async function copyCode() {
    const text = preRef.current?.textContent ?? '';
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      if (copyTimer.current !== null) window.clearTimeout(copyTimer.current);
      copyTimer.current = window.setTimeout(() => {
        copyTimer.current = null;
        setCopied(false);
      }, 1400);
    } catch {
      /* Clipboard access may be unavailable in restricted contexts. */
    }
  }

  return (
    <div className="code-block">
      <button
        type="button"
        className="code-copy-button"
        aria-label={copied ? 'Copied code block' : 'Copy code block'}
        title={copied ? 'Copied' : 'Copy code'}
        onClick={() => void copyCode()}
      >
        <Icon name={copied ? 'check' : 'copy'} size={14} />
      </button>
      <span className="sr-only" role="status">{copied ? 'Copied code block to clipboard' : ''}</span>
      <pre ref={preRef}>{children}</pre>
    </div>
  );
}

// Animate only received text. Saved messages and terminal states render immediately.
export const StreamingMarkdown = memo(function StreamingMarkdown({ text, streaming }: { text: string; streaming: boolean }) {
  const [visible, setVisible] = useState(text);
  const current = useRef(text);
  useEffect(() => {
    if (!streaming || !text.startsWith(current.current) || window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) {
      current.current = text;
      setVisible(text);
      return;
    }
    const start = current.current.length;
    const started = performance.now();
    let frame = 0;
    let lastPaint = 0;
    function reveal(now: number) {
      if (now - lastPaint >= 32) {
        const progress = Math.min(1, (now - started) / 360);
        let end = start + Math.ceil((text.length - start) * progress);
        // Avoid splitting UTF-16 pairs when revealing emoji.
        if (end < text.length && /[\uD800-\uDBFF]/.test(text[end - 1] ?? '')) end++;
        current.current = text.slice(0, end);
        setVisible(current.current);
        lastPaint = now;
      }
      if (current.current.length < text.length) frame = requestAnimationFrame(reveal);
    }
    frame = requestAnimationFrame(reveal);
    return () => cancelAnimationFrame(frame);
  }, [text, streaming]);

  return <div><ReactMarkdown remarkPlugins={[remarkGfm]} components={{
    img: ({ alt }) => <span className="remote-image-blocked">Image not loaded{alt ? `: ${alt}` : ''}</span>,
    a: ({ href, children }) => {
      const safeUrl = toSafeExternalUrl(href);
      if (!safeUrl) return <span className="blocked-link">{children}</span>;
      return <a href={safeUrl} target="_blank" rel="noopener noreferrer" onClick={event => { event.preventDefault(); void openExternalLink(safeUrl); }}>{children}</a>;
    },
    pre: ({ children }) => <CodeBlock>{children}</CodeBlock>,
  }}>{visible}</ReactMarkdown></div>;
});
