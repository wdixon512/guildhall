import { afterEach, describe, expect, it, vi } from 'vitest';
import { parseDiscovery, parseReadiness, request } from './api';

afterEach(() => vi.unstubAllGlobals());
describe('host API client', () => {
  it('sends the session token only as a header to the host', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response('{"status":"ok","apiVersion":1}'));
    vi.stubGlobal('fetch', fetch);
    const controller = new AbortController();
    await request({ hostUrl: 'http://127.0.0.1:43120', token: 'session-secret' }, '/health', controller.signal);
    const [url, options] = fetch.mock.calls[0];
    expect(String(url)).toBe('http://127.0.0.1:43120/health');
    expect(options.headers.Authorization).toBe('Bearer session-secret');
    expect(options.signal).toBe(controller.signal);
    expect(options.redirect).toBe('error');
  });
  it('accepts a structured readiness failure but rejects authentication failures', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response('{"status":"unavailable","reason":"migration_required"}', { status: 503 }))
      .mockResolvedValueOnce(new Response('', { status: 401 })));
    const config = { hostUrl: 'http://127.0.0.1:43120', token: 'token' };
    const signal = new AbortController().signal;
    expect(parseReadiness(await request(config, '/ready', signal)).reason).toBe('migration_required');
    await expect(request(config, '/health', signal)).rejects.toThrow();
  });
  it('rejects malformed contract data', () => {
    expect(() => parseReadiness({ status: 'unavailable', reason: '<private exception>' })).toThrow();
    expect(() => parseDiscovery({ providerId: 'ollama', status: 'available', models: [null] })).toThrow();
  });
});
