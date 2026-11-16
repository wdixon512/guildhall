/** Parse only explicit model thinking delimiters; never guess from prose. */
export function splitThinking(text: string, streaming = false) {
  let answer = '', thinking = '', inside = false, marked = false;
  // Some model templates omit the opening delimiter from the returned content.
  const firstOpen = text.indexOf('<think>'), firstClose = text.indexOf('</think>');
  if (firstClose >= 0 && (firstOpen < 0 || firstClose < firstOpen)) { inside = true; marked = true; }
  let cursor = 0;
  const tags = /<think>|<\/think>/g;
  for (const match of text.matchAll(tags)) {
    const chunk = text.slice(cursor, match.index);
    if (inside) thinking += chunk; else answer += chunk;
    inside = match[0] === '<think>'; marked = true;
    cursor = match.index! + match[0].length;
  }
  let tail = text.slice(cursor);
  if (streaming) {
    for (let length = Math.min(7, tail.length); length > 0; length--) {
      const suffix = tail.slice(-length);
      if ('<think>'.startsWith(suffix) || '</think>'.startsWith(suffix)) { tail = tail.slice(0, -length); break; }
    }
  }
  if (inside) thinking += tail; else answer += tail;
  return { answer: answer.trim(), thinking: thinking.trim(), marked, pending: inside };
}
