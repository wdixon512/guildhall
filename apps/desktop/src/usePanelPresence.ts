import { useEffect, useState } from 'react';

// One short timer only when a panel closes; no animation loop or idle work.
export function usePanelPresence(open: boolean) {
  const [mounted, setMounted] = useState(open);
  useEffect(() => {
    if (open) { setMounted(true); return; }
    if (!mounted) return;
    const delay = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ? 0 : 180;
    const timer = window.setTimeout(() => setMounted(false), delay);
    return () => window.clearTimeout(timer);
  }, [open, mounted]);
  return { present: open || mounted, closing: !open };
}
