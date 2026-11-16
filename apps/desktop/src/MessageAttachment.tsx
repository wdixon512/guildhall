import { useEffect, useState } from 'react';
import { requestBlob } from './api';
import type { Attachment, Bootstrap } from './api';

export function MessageAttachment({ attachment, config }: { attachment: Attachment; config: Bootstrap | null }) {
  const [url, setUrl] = useState('');
  const [previewFailed, setPreviewFailed] = useState(false);
  const [downloadFailed, setDownloadFailed] = useState(false);
  const isImage = /^image\/(png|jpeg|gif|webp)$/i.test(attachment.contentType);
  useEffect(() => {
    if (!isImage || !config) return;
    const controller = new AbortController(); let objectUrl = '';
    void requestBlob(config, `/attachments/${attachment.id}`, controller.signal).then(blob => {
      objectUrl = URL.createObjectURL(blob); setUrl(objectUrl);
    }).catch(() => { if (!controller.signal.aborted) setPreviewFailed(true); });
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [attachment.id, config, isImage]);
  const size = attachment.length < 1024 * 1024 ? `${Math.max(1, Math.round(attachment.length / 1024))} KB` : `${(attachment.length / (1024 * 1024)).toFixed(1)} MB`;
  async function download() {
    if (!config) return;
    setDownloadFailed(false);
    try {
      const blob = await requestBlob(config, `/attachments/${attachment.id}`, new AbortController().signal);
      const objectUrl = URL.createObjectURL(blob);
      const anchor = document.createElement('a'); anchor.href = objectUrl; anchor.download = attachment.fileName; anchor.click();
      window.setTimeout(() => URL.revokeObjectURL(objectUrl), 30000);
    } catch { setDownloadFailed(true); }
  }
  const button = <button aria-label={`Download ${attachment.fileName}`} title="Download attachment" onClick={() => void download()}><svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="M12 3v12m-5-5 5 5 5-5M5 16v5h14v-5" /></svg></button>;
  return isImage ? <div className="inline-attachment">
    {previewFailed ? <div className="preview-failed" role="status">Image preview unavailable.</div> : url ? <img src={url} alt={attachment.fileName} loading="lazy" /> : <span>Loading image…</span>}
    <div className="attachment-caption"><small>{attachment.fileName} · {size}</small>{button}</div>
    {downloadFailed && <small role="alert">Download failed. Check the local host connection and try again.</small>}
  </div> : <div className="file-attachment-wrap"><button className="file-attachment" aria-label={`Download ${attachment.fileName}`} onClick={() => void download()}>▤ &nbsp; {attachment.fileName} <small>{size}</small></button>{downloadFailed && <small role="alert">Download failed. Check the local host connection and try again.</small>}</div>;
}
