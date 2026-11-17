export type IconName = 'chat' | 'council' | 'folder' | 'branch' | 'spark' | 'settings' | 'search' | 'plus' | 'close' | 'panel' | 'attach' | 'send' | 'chevron' | 'archive' | 'edit' | 'trash' | 'gate' | 'connections' | 'clock' | 'shield' | 'copy' | 'check' | 'play' | 'pause' | 'resume' | 'refresh' | 'stream' | 'open' | 'expand' | 'key' | 'eye' | 'eyeOff';
const paths: Record<IconName, string> = {
  connections: 'M8 3v5M16 3v5M5 8h14v3a7 7 0 0 1-14 0V8ZM12 18v4',
  chat: 'M21 11.5a8.5 8.5 0 0 1-8.5 8.5H8l-5 2 1.7-5A8.5 8.5 0 1 1 21 11.5Z',
  council: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8ZM22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75',
  folder: 'M3 6h7l2 3h9v11H3V6Z', branch: 'M6 3v12M18 9a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM6 21a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM18 9a9 9 0 0 1-9 9', spark: 'm12 2 2.8 7.2L22 12l-7.2 2.8L12 22l-2.8-7.2L2 12l7.2-2.8L12 2Z',
  settings: 'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09a1.65 1.65 0 0 0-1-1.51 1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09a1.65 1.65 0 0 0 1.51-1 1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33h.09a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82v.09a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1Z',
  search: 'M17 10a7 7 0 1 1-14 0 7 7 0 0 1 14 0Zm-2 5 6 6', plus: 'M12 5v14M5 12h14', close: 'M18 6 6 18M6 6l12 12',
  panel: 'M3 4h18v16H3zM9 4v16', attach: 'M21.44 11.05 12.25 20.24a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.82-2.82l8.49-8.48',
  send: 'm3 11 18-8-8 18-2-8-8-2ZM11 13 21 3', chevron: 'm9 5 7 7-7 7',
  archive: 'M3 4h18v4H3zM5 8v12h14V8M9 12h6',
  edit: 'M12 20h9M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z',
  trash: 'M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6M10 11v6M14 11v6',
  gate: 'M3 21V10L12 2l9 8v11h-5V11l-4-4-4 4v10H3ZM1 22h22',
  clock: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20ZM12 6v6l4 2',
  shield: 'M12 22s8-4 8-11V5l-8-3-8 3v6c0 7 8 11 8 11ZM9 12l2 2 4-4',
  copy: 'M8 8h12v13H8zM16 8V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h3',
  check: 'm5 12 4 4L19 6',
  play: 'M7 4.5v15l12-7.5-12-7.5Z',
  pause: 'M7 5h3.5v14H7zM13.5 5H17v14h-3.5z',
  resume: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20ZM10 8l6 4-6 4V8Z',
  refresh: 'M21 12a9 9 0 1 1-2.64-6.36M21 3v6h-6',
  stream: 'M22 12h-4l-3 9L9 3l-3 9H2',
  open: 'M15 3h6v6M10 14 21 3M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6',
  expand: 'M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7',
  key: 'M7.5 19a4.5 4.5 0 1 0 0-9 4.5 4.5 0 0 0 0 9ZM10.7 11.3 20 2M17 5l2.5 2.5',
  eye: 'M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7ZM12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z',
  eyeOff: 'M9.88 9.88a3 3 0 1 0 4.24 4.24M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61M2 2l20 20',
};
export function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
  return <svg aria-hidden="true" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d={paths[name]} /></svg>;
}
