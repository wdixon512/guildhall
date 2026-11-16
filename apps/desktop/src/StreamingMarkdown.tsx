import { memo, useEffect, useRef, useState } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';

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
    a: ({ href, children }) => <a href={href} target="_blank" rel="noopener noreferrer">{children}</a>,
  }}>{visible}</ReactMarkdown></div>;
});
