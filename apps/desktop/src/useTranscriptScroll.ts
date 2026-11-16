import { useCallback, useLayoutEffect, useRef } from 'react';

export function useTranscriptScroll(conversationId: string | null, messages: unknown) {
  const transcript = useRef<HTMLDivElement>(null);
  const content = useRef<HTMLDivElement>(null);
  const following = useRef(true);
  const lastTop = useRef(0);
  const previousConversation = useRef<string | null>(null);
  const pinToBottom = useCallback(() => {
    following.current = true;
    const element = transcript.current;
    if (element) { element.scrollTop = element.scrollHeight; lastTop.current = element.scrollTop; }
  }, []);
  useLayoutEffect(() => {
    if (previousConversation.current !== conversationId) {
      previousConversation.current = conversationId;
      pinToBottom();
    } else if (following.current) pinToBottom();
  }, [conversationId, messages, pinToBottom]);
  useLayoutEffect(() => {
    if (!window.ResizeObserver || !content.current || !transcript.current) return;
    const observer = new ResizeObserver(() => { if (following.current) pinToBottom(); });
    observer.observe(content.current);
    observer.observe(transcript.current);
    return () => observer.disconnect();
  }, [pinToBottom]);
  return {
    transcript, content, pinToBottom,
    onScroll: () => {
      const element = transcript.current;
      if (!element) return;
      const gap = element.scrollHeight - element.scrollTop - element.clientHeight;
      // Layout/entrance-animation settling can clamp scrollTop downward even
      // though the viewport is still at the bottom. That is not an upward read.
      if (gap <= 1) following.current = true;
      else if (element.scrollTop < lastTop.current - 1) following.current = false;
      else if (gap <= 24 && element.scrollTop > lastTop.current) following.current = true;
      lastTop.current = element.scrollTop;
    },
    onWheel: (event: React.WheelEvent) => { if (event.deltaY < 0) following.current = false; },
    onKeyDown: (event: React.KeyboardEvent) => {
      if (['ArrowUp', 'PageUp', 'Home'].includes(event.key)) following.current = false;
      if (event.key === 'End') pinToBottom();
    },
  };
}
