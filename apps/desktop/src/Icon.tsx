export type IconName = 'chat' | 'council' | 'folder' | 'spark' | 'settings' | 'search' | 'plus' | 'close' | 'panel' | 'attach' | 'send' | 'chevron' | 'archive' | 'edit' | 'trash' | 'gate' | 'connections' | 'clock' | 'shield';
const paths: Record<IconName, string> = {
  connections: 'M8 3v5M16 3v5M5 8h14v3a7 7 0 0 1-14 0V8ZM12 18v4',
  chat: 'M21 11.5a8.5 8.5 0 0 1-8.5 8.5H8l-5 2 1.7-5A8.5 8.5 0 1 1 21 11.5Z',
  council: 'M4 4h4v16H4zM10 3h4v17h-4zM16 5h4v15h-4z',
  folder: 'M3 6h7l2 3h9v11H3V6Z', spark: 'm12 2 2.8 7.2L22 12l-7.2 2.8L12 22l-2.8-7.2L2 12l7.2-2.8L12 2Z',
  settings: 'm9 3 1-1h4l1 3 3 1 3 1v4l-2 2-1 3-2 3h-4l-2-2-3-1-3-2v-4l2-2 1-3 2-2ZM15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z',
  search: 'M17 10a7 7 0 1 1-14 0 7 7 0 0 1 14 0Zm-2 5 6 6', plus: 'M12 4v16M4 12h16', close: 'm6 6 12 12M18 6 6 18',
  panel: 'M3 4h18v16H3zM9 4v16', attach: 'M21.44 11.05 12.25 20.24a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.82-2.82l8.49-8.48',
  send: 'm3 11 18-8-8 18-2-8-8-2ZM11 13 21 3', chevron: 'm9 5 7 7-7 7',
  archive: 'M3 4h18v4H3zM5 8v12h14V8M9 12h6', edit: 'm4 16 12-12 4 4L8 20H4v-4ZM13 7l4 4',
  trash: 'M4 6h16M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7M14 10v7',
  gate: 'M3 21V10L12 2l9 8v11h-5V11l-4-4-4 4v10H3ZM1 22h22',
  clock: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20ZM12 6v6l4 2',
  shield: 'M12 22s8-4 8-11V5l-8-3-8 3v6c0 7 8 11 8 11ZM9 12l2 2 4-4',
};
export function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
  return <svg aria-hidden="true" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"><path d={paths[name]} /></svg>;
}
