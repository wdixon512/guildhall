import { useEffect, useRef, useState } from 'react';
import { HostRequestError, requestJson } from './api';
import type { AgentProfile, Bootstrap, DurableTaskRunView, RegisteredProject, ScheduledAutomationView } from './api';
import { Portrait } from './CouncilMember';
import type { MemberIdentity } from './CouncilMember';
import { Icon } from './Icon';
import { TextEditorModal } from './TextEditorModal';

function formatUtcTimestamp(iso: string): string {
  const normalized = /([zZ]|[+-]\d{2}:?\d{2})$/.test(iso) ? iso : `${iso}Z`;
  return new Date(normalized).toLocaleString();
}

function toOwnerIdentity(profile: AgentProfile): MemberIdentity {
  return {
    name: profile.name,
    role: profile.role,
    portrait: profile.portraitId,
    customPortrait: profile.customPortrait ?? '',
    accent: profile.accent,
    instructions: profile.instructions,
  };
}

interface ScheduledTasksPanelProps {
  config: Bootstrap | null;
  profiles: AgentProfile[];
  projects: RegisteredProject[];
  onOpenConversation?: (conversationId: string) => void;
}

type ScheduleCadence = 'every_15m' | 'every_30m' | 'hourly' | 'daily' | 'weekly' | 'monthly' | 'custom';

const CADENCE_OPTIONS: { value: ScheduleCadence; label: string }[] = [
  { value: 'every_15m', label: 'Every 15 minutes' },
  { value: 'every_30m', label: 'Every 30 minutes' },
  { value: 'hourly', label: 'Hourly' },
  { value: 'daily', label: 'Daily' },
  { value: 'weekly', label: 'Weekly' },
  { value: 'monthly', label: 'Monthly' },
  { value: 'custom', label: 'Custom cron…' },
];

const WEEKDAYS = [
  { value: 0, label: 'Sun' },
  { value: 1, label: 'Mon' },
  { value: 2, label: 'Tue' },
  { value: 3, label: 'Wed' },
  { value: 4, label: 'Thu' },
  { value: 5, label: 'Fri' },
  { value: 6, label: 'Sat' },
];

function buildCronExpression(
  cadence: ScheduleCadence,
  hourlyMinute: number,
  timeOfDay: string,
  weekdays: number[],
  monthDay: number,
  customCron: string,
): string {
  const [rawHour, rawMinute] = timeOfDay.split(':');
  const hour = Math.min(23, Math.max(0, Number.parseInt(rawHour ?? '9', 10) || 0));
  const minute = Math.min(59, Math.max(0, Number.parseInt(rawMinute ?? '0', 10) || 0));
  const safeHourlyMin = Math.min(59, Math.max(0, hourlyMinute || 0));
  const safeMonthDay = Math.min(31, Math.max(1, monthDay || 1));

  switch (cadence) {
    case 'every_15m':
      return '*/15 * * * *';
    case 'every_30m':
      return '*/30 * * * *';
    case 'hourly':
      return `${safeHourlyMin} * * * *`;
    case 'daily':
      return `${minute} ${hour} * * *`;
    case 'weekly': {
      const sorted = [...new Set(weekdays)].sort((a, b) => a - b);
      return `${minute} ${hour} * * ${sorted.length > 0 ? sorted.join(',') : '1'}`;
    }
    case 'monthly':
      return `${minute} ${hour} ${safeMonthDay} * *`;
    case 'custom':
      return customCron.trim();
  }
}

function parseCronExpression(cron: string): {
  cadence: ScheduleCadence;
  hourlyMinute: number;
  timeOfDay: string;
  weekdays: number[];
  monthDay: number;
  customCron: string;
} {
  const parts = cron.trim().split(/\s+/);
  const fallback = {
    cadence: 'custom' as ScheduleCadence,
    hourlyMinute: 0,
    timeOfDay: '09:00',
    weekdays: [1],
    monthDay: 1,
    customCron: cron,
  };
  if (parts.length !== 5) return fallback;
  const [min, hr, dom, mon, dow] = parts;
  if (min === '*/15' && hr === '*' && dom === '*' && mon === '*' && dow === '*') {
    return { ...fallback, cadence: 'every_15m' };
  }
  if (min === '*/30' && hr === '*' && dom === '*' && mon === '*' && dow === '*') {
    return { ...fallback, cadence: 'every_30m' };
  }
  if (/^\d+$/.test(min) && hr === '*' && dom === '*' && mon === '*' && dow === '*') {
    return { ...fallback, cadence: 'hourly', hourlyMinute: Math.min(59, Math.max(0, Number.parseInt(min, 10))) };
  }
  if (/^\d+$/.test(min) && /^\d+$/.test(hr) && mon === '*') {
    const h = Math.min(23, Math.max(0, Number.parseInt(hr, 10)));
    const m = Math.min(59, Math.max(0, Number.parseInt(min, 10)));
    const timeOfDay = `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
    if (dom === '*' && dow === '*') {
      return { ...fallback, cadence: 'daily', timeOfDay };
    }
    if (dom === '*' && /^\d+(,\d+)*$/.test(dow)) {
      const parsedDays = dow.split(',').map(d => Number.parseInt(d, 10)).filter(d => d >= 0 && d <= 6);
      if (parsedDays.length > 0) {
        return { ...fallback, cadence: 'weekly', timeOfDay, weekdays: parsedDays };
      }
    }
    if (/^\d+$/.test(dom) && dow === '*') {
      return { ...fallback, cadence: 'monthly', timeOfDay, monthDay: Math.min(31, Math.max(1, Number.parseInt(dom, 10))) };
    }
  }
  return fallback;
}

function describeRunError(errorCode?: string | null): string {
  switch (errorCode) {
    case 'model_missing':
      return 'No model is configured for this council member. Open the member profile to choose a default model.';
    case 'provider_unavailable':
      return 'The configured backing engine is unavailable. Check Connections and try again.';
    case 'provider_auth_failed':
      return 'Provider authentication failed. Check the configured API key or CLI sign-in in Connections.';
    case 'cli_not_installed':
      return 'The configured CLI harness was not found on PATH. Check Connections to install it.';
    case 'generation_timeout':
      return 'The run timed out before finishing.';
    case 'context_exceeded':
      return 'The prompt and context exceeded the model context window.';
    case 'schedule_budget_exceeded':
      return 'Estimated tokens exceeded this schedule’s token budget.';
    case 'provider_error':
      return 'The backing model provider returned an error before producing output.';
    default:
      return errorCode ? `Run failed (${errorCode}).` : 'The run failed before producing output.';
  }
}

export function ScheduledTasksPanel({ config, profiles, projects, onOpenConversation }: ScheduledTasksPanelProps) {
  const [schedules, setSchedules] = useState<ScheduledAutomationView[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [busyId, setBusyId] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const [editingSchedule, setEditingSchedule] = useState<ScheduledAutomationView | null>(null);

  const [name, setName] = useState('');
  const [agentId, setAgentId] = useState('');
  const [projectId, setProjectId] = useState('');
  const [prompt, setPrompt] = useState('');
  const [cadence, setCadence] = useState<ScheduleCadence>('daily');
  const [hourlyMinute, setHourlyMinute] = useState(0);
  const [timeOfDay, setTimeOfDay] = useState('09:00');
  const [weekdays, setWeekdays] = useState<number[]>([1]);
  const [monthDay, setMonthDay] = useState(1);
  const [customCron, setCustomCron] = useState('0 * * * *');
  const [misfirePolicy, setMisfirePolicy] = useState<'RunOnce' | 'Skip' | 'CatchUpBounded'>('RunOnce');
  const [maxBudgetTokens, setMaxBudgetTokens] = useState('');

  const [selectedScheduleId, setSelectedScheduleId] = useState<string | null>(null);
  const [runs, setRuns] = useState<DurableTaskRunView[]>([]);
  const [runsLoading, setRunsLoading] = useState(false);
  const [streamingRunId, setStreamingRunId] = useState<string | null>(null);
  const [streamText, setStreamText] = useState('');
  const [streamStatus, setStreamStatus] = useState('');
  const [streamError, setStreamError] = useState('');
  const [streamModalOpen, setStreamModalOpen] = useState(false);
  const selectedScheduleIdRef = useRef<string | null>(null);
  const streamAbort = useRef<AbortController | null>(null);
  const runsAbort = useRef<AbortController | null>(null);
  const formRef = useRef<HTMLFormElement | null>(null);
  const nameInputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    selectedScheduleIdRef.current = selectedScheduleId;
  }, [selectedScheduleId]);

  const activeProfiles = profiles.filter(p => !p.isArchived);

  useEffect(() => {
    if (!agentId && activeProfiles.length > 0) {
      setAgentId(activeProfiles[0].id);
    }
  }, [activeProfiles, agentId]);

  useEffect(() => {
    if (!config) {
      setLoading(false);
      return;
    }
    const ac = new AbortController();
    void loadSchedules(ac.signal);
    return () => {
      ac.abort();
      runsAbort.current?.abort();
      streamAbort.current?.abort();
    };
  }, [config]);

  async function loadSchedules(signal?: AbortSignal) {
    if (!config) return;
    setLoading(true);
    setError('');
    try {
      const data = await requestJson<ScheduledAutomationView[]>(config, '/schedules', 'GET', signal ?? new AbortController().signal);
      setSchedules(data);
    } catch (reason) {
      if (reason instanceof DOMException && reason.name === 'AbortError') return;
      setError(reason instanceof HostRequestError && reason.serverMessage ? reason.serverMessage : 'Could not load scheduled tasks.');
    } finally {
      setLoading(false);
    }
  }

  async function loadRuns(scheduleId: string) {
    if (!config) return;
    runsAbort.current?.abort();
    const ac = new AbortController();
    runsAbort.current = ac;
    if (selectedScheduleId !== scheduleId) {
      streamAbort.current?.abort();
      setStreamingRunId(null);
      setStreamModalOpen(false);
      setStreamText('');
      setStreamStatus('');
      setStreamError('');
    }
    selectedScheduleIdRef.current = scheduleId;
    setSelectedScheduleId(scheduleId);
    setRunsLoading(true);
    setError('');
    try {
      const data = await requestJson<DurableTaskRunView[]>(config, `/schedules/${encodeURIComponent(scheduleId)}/runs`, 'GET', ac.signal);
      setRuns(data);
      const activeRun = data.find(r => r.status === 'Running' || r.status === 'Queued' || r.status === 'NeedsApproval');
      if (activeRun) {
        void attachRunStream(activeRun.id, scheduleId);
      }
    } catch (reason) {
      if (reason instanceof DOMException && reason.name === 'AbortError') return;
      setError(reason instanceof HostRequestError && reason.serverMessage ? reason.serverMessage : 'Could not load run history.');
    } finally {
      setRunsLoading(false);
    }
  }

  async function attachRunStream(taskRunId: string, scheduleIdOverride?: string, seedErrorCode?: string | null, openModal = false) {
    if (!config) return;
    streamAbort.current?.abort();
    const ac = new AbortController();
    streamAbort.current = ac;
    const targetScheduleId = scheduleIdOverride ?? selectedScheduleIdRef.current;
    setStreamingRunId(taskRunId);
    if (openModal) setStreamModalOpen(true);
    setStreamStatus('Connecting…');
    setStreamText('');
    setStreamError(seedErrorCode ? describeRunError(seedErrorCode) : '');

    try {
      const response = await fetch(new URL(`/schedules/runs/${encodeURIComponent(taskRunId)}/stream`, config.hostUrl), {
        headers: { Authorization: `Bearer ${config.token}` },
        signal: ac.signal,
        cache: 'no-store',
      });
      if (!response.ok || !response.body) {
        setStreamStatus('Stream unavailable');
        setStreamError('Live stream is unavailable right now. Check Run history for recorded status.');
        return;
      }
      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      while (true) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        const frames = buffer.split('\n\n');
        buffer = frames.pop() ?? '';
        for (const frame of frames) {
          const dataLine = frame.split('\n').find(l => l.startsWith('data: '));
          if (!dataLine) continue;
          try {
            const payload = JSON.parse(dataLine.slice(6)) as {
              status?: string;
              text?: string;
              errorCode?: string | null;
              summaryText?: string | null;
            };
            if (payload.status) setStreamStatus(payload.status);
            if (typeof payload.text === 'string' && payload.text) {
              setStreamText(payload.text);
            } else if (typeof payload.summaryText === 'string' && payload.summaryText) {
              setStreamText(prev => prev || payload.summaryText!);
            }
            if (payload.errorCode) {
              setStreamError(describeRunError(payload.errorCode));
            } else if (payload.status === 'Failed') {
              setStreamError(describeRunError(null));
            }
          } catch {
            // ignore malformed frame
          }
        }
      }
      if (targetScheduleId) void loadRuns(targetScheduleId);
      void loadSchedules();
    } catch (reason) {
      if (reason instanceof DOMException && reason.name === 'AbortError') return;
      setStreamStatus('Disconnected');
      setStreamError(prev => prev || 'Disconnected from live run stream.');
    }
  }

  function resetForm() {
    setEditingSchedule(null);
    setName('');
    setPrompt('');
    setMaxBudgetTokens('');
    setProjectId('');
    setCadence('daily');
    setHourlyMinute(0);
    setTimeOfDay('09:00');
    setWeekdays([1]);
    setMonthDay(1);
    setCustomCron('0 * * * *');
    setMisfirePolicy('RunOnce');
    if (activeProfiles.length > 0) setAgentId(activeProfiles[0].id);
  }

  function startEdit(item: ScheduledAutomationView) {
    const parsed = parseCronExpression(item.cronExpression);
    setEditingSchedule(item);
    setName(item.name);
    setAgentId(item.agentDefinitionId);
    setProjectId(item.projectId ?? '');
    setPrompt(item.prompt);
    setCadence(parsed.cadence);
    setHourlyMinute(parsed.hourlyMinute);
    setTimeOfDay(parsed.timeOfDay);
    setWeekdays(parsed.weekdays);
    setMonthDay(parsed.monthDay);
    setCustomCron(parsed.customCron);
    setMisfirePolicy((item.misfirePolicy as 'RunOnce' | 'Skip' | 'CatchUpBounded') || 'RunOnce');
    setMaxBudgetTokens(item.maxBudgetTokens ? String(item.maxBudgetTokens) : '');
    setShowCreate(true);
    setError('');
    requestAnimationFrame(() => {
      formRef.current?.scrollIntoView?.({ block: 'nearest' });
      nameInputRef.current?.focus();
    });
  }

  async function handleCreate(event: React.FormEvent) {
    event.preventDefault();
    if (!config || !agentId) return;
    setBusyId(editingSchedule ? editingSchedule.id : 'create');
    setError('');
    try {
      const cronExpression = buildCronExpression(cadence, hourlyMinute, timeOfDay, weekdays, monthDay, customCron);
      const budget = maxBudgetTokens.trim() ? Number.parseInt(maxBudgetTokens.trim(), 10) : null;
      if (editingSchedule) {
        await requestJson<ScheduledAutomationView>(config, `/schedules/${encodeURIComponent(editingSchedule.id)}`, 'PATCH', new AbortController().signal, {
          expectedRevision: editingSchedule.revision,
          name: name.trim(),
          agentDefinitionId: agentId,
          prompt: prompt.trim(),
          cronExpression,
          timezoneId: 'Local',
          misfirePolicy,
          maxBudgetTokens: budget && Number.isFinite(budget) ? budget : 0,
          projectId: projectId || '00000000-0000-0000-0000-000000000000',
        });
      } else {
        await requestJson<ScheduledAutomationView>(config, '/schedules', 'POST', new AbortController().signal, {
          name: name.trim(),
          agentDefinitionId: agentId,
          prompt: prompt.trim(),
          cronExpression,
          timezoneId: 'Local',
          misfirePolicy,
          maxBudgetTokens: Number.isFinite(budget) ? budget : null,
          projectId: projectId || null,
        });
      }
      const editedId = editingSchedule?.id;
      resetForm();
      setShowCreate(false);
      await loadSchedules();
      if (editedId) {
        requestAnimationFrame(() => document.getElementById(`schedule-edit-${editedId}`)?.focus());
      }
    } catch (reason) {
      setError(reason instanceof HostRequestError && reason.serverMessage ? reason.serverMessage : editingSchedule ? 'Could not update schedule.' : 'Could not create schedule.');
    } finally {
      setBusyId(null);
    }
  }

  async function handleTogglePause(item: ScheduledAutomationView) {
    if (!config) return;
    setBusyId(item.id);
    setError('');
    try {
      await requestJson<ScheduledAutomationView>(config, `/schedules/${encodeURIComponent(item.id)}`, 'PATCH', new AbortController().signal, {
        expectedRevision: item.revision,
        isPaused: !item.isPaused,
      });
      await loadSchedules();
    } catch (reason) {
      setError(reason instanceof HostRequestError && reason.serverMessage ? reason.serverMessage : 'Could not update schedule.');
    } finally {
      setBusyId(null);
    }
  }

  async function handleRunNow(item: ScheduledAutomationView) {
    if (!config) return;
    setBusyId(item.id);
    setError('');
    try {
      const run = await requestJson<DurableTaskRunView>(config, `/schedules/${encodeURIComponent(item.id)}/run-now`, 'POST', new AbortController().signal);
      await loadRuns(item.id);
      void attachRunStream(run.id, item.id);
    } catch (reason) {
      setError(reason instanceof HostRequestError && reason.serverMessage ? reason.serverMessage : 'Could not trigger run.');
    } finally {
      setBusyId(null);
    }
  }

  async function handleDelete(item: ScheduledAutomationView) {
    if (!config) return;
    setBusyId(item.id);
    setError('');
    try {
      await requestJson<null>(config, `/schedules/${encodeURIComponent(item.id)}`, 'DELETE', new AbortController().signal);
      setConfirmDeleteId(null);
      if (selectedScheduleId === item.id) {
        setSelectedScheduleId(null);
        setRuns([]);
      }
      await loadSchedules();
    } catch (reason) {
      setError(reason instanceof HostRequestError && reason.serverMessage ? reason.serverMessage : 'Could not delete schedule.');
    } finally {
      setBusyId(null);
    }
  }

  const renderScheduleForm = () => (
    <form ref={formRef} className="schedule-create-form" onSubmit={handleCreate}>
      <h4>{editingSchedule ? `Edit schedule: ${editingSchedule.name}` : 'New schedule'}</h4>
      <label>
        <span>Task name</span>
        <input ref={nameInputRef} type="text" required maxLength={120} value={name} onChange={e => setName(e.target.value)} placeholder="Daily repo summary" />
      </label>
      <div className="schedule-form-row">
        <label>
          <span>Council member</span>
          <select value={agentId} onChange={e => setAgentId(e.target.value)}>
            {activeProfiles.map(p => (
              <option key={p.id} value={p.id}>{p.name} ({p.role})</option>
            ))}
            {editingSchedule && !activeProfiles.some(p => p.id === editingSchedule.agentDefinitionId) && (
              <option value={editingSchedule.agentDefinitionId}>{editingSchedule.agentName} (Archived)</option>
            )}
          </select>
        </label>
        <label>
          <span>Schedule</span>
          <select value={cadence} onChange={e => setCadence(e.target.value as ScheduleCadence)}>
            {CADENCE_OPTIONS.map(p => (
              <option key={p.value} value={p.value}>{p.label}</option>
            ))}
          </select>
        </label>
      </div>
      {cadence === 'hourly' && (
        <label>
          <span>Minute past the hour (0–59)</span>
          <input
            type="number"
            min={0}
            max={59}
            value={hourlyMinute}
            onChange={e => setHourlyMinute(Math.min(59, Math.max(0, Number.parseInt(e.target.value, 10) || 0)))}
          />
        </label>
      )}
      {(cadence === 'daily' || cadence === 'weekly' || cadence === 'monthly') && (
        <div className="schedule-form-row">
          <label>
            <span>Time of day</span>
            <input type="time" required value={timeOfDay} onChange={e => setTimeOfDay(e.target.value || '09:00')} />
          </label>
          {cadence === 'monthly' && (
            <label>
              <span>Day of month (1–31)</span>
              <input
                type="number"
                min={1}
                max={31}
                value={monthDay}
                onChange={e => setMonthDay(Math.min(31, Math.max(1, Number.parseInt(e.target.value, 10) || 1)))}
              />
            </label>
          )}
        </div>
      )}
      {cadence === 'weekly' && (
        <div className="schedule-weekdays-field">
          <span>Days of week</span>
          <div className="schedule-day-chips" role="group" aria-label="Days of week">
            {WEEKDAYS.map(day => {
              const selected = weekdays.includes(day.value);
              return (
                <button
                  key={day.value}
                  type="button"
                  className={selected ? 'selected' : ''}
                  aria-pressed={selected}
                  onClick={() => {
                    setWeekdays(curr =>
                      curr.includes(day.value)
                        ? (curr.length > 1 ? curr.filter(d => d !== day.value) : curr)
                        : [...curr, day.value]
                    );
                  }}
                >
                  {day.label}
                </button>
              );
            })}
          </div>
        </div>
      )}
      {cadence === 'custom' && (
        <label>
          <span>5-field cron (min hr dom mon dow)</span>
          <input type="text" required value={customCron} onChange={e => setCustomCron(e.target.value)} placeholder="*/30 * * * *" />
        </label>
      )}
      <small className="muted">
        Cron: <code>{buildCronExpression(cadence, hourlyMinute, timeOfDay, weekdays, monthDay, customCron)}</code> (local time)
      </small>
      <div className="schedule-form-row">
        <label>
          <span>Missed-run policy</span>
          <select value={misfirePolicy} onChange={e => setMisfirePolicy(e.target.value as 'RunOnce' | 'Skip' | 'CatchUpBounded')}>
            <option value="RunOnce">Run once after wake</option>
            <option value="Skip">Skip missed slots</option>
            <option value="CatchUpBounded">Catch up (max 3)</option>
          </select>
        </label>
        <label>
          <span>Project scope (optional)</span>
          <select value={projectId} onChange={e => setProjectId(e.target.value)}>
            <option value="">None</option>
            {projects.map(p => (
              <option key={p.id} value={p.id}>{p.name}</option>
            ))}
          </select>
        </label>
      </div>
      <label>
        <span>Max token budget (optional)</span>
        <input type="number" min={512} max={524288} value={maxBudgetTokens} onChange={e => setMaxBudgetTokens(e.target.value)} placeholder="Inherit member/session cap" />
      </label>
      <label>
        <span>Task prompt</span>
        <textarea required rows={3} maxLength={8000} value={prompt} onChange={e => setPrompt(e.target.value)} placeholder="Review recent commits and summarize open risks." />
      </label>
      <div className="schedule-actions">
        <button type="submit" className="primary" disabled={busyId === (editingSchedule ? editingSchedule.id : 'create') || !config || !agentId || !name.trim() || !prompt.trim()}>
          {busyId === (editingSchedule ? editingSchedule.id : 'create') ? 'Saving…' : editingSchedule ? 'Save schedule' : 'Create schedule'}
        </button>
        <button type="button" onClick={() => {
          const editedId = editingSchedule?.id;
          resetForm();
          setShowCreate(false);
          if (editedId) {
            requestAnimationFrame(() => document.getElementById(`schedule-edit-${editedId}`)?.focus());
          }
        }}>
          {editingSchedule ? 'Cancel edit' : 'Cancel'}
        </button>
      </div>
      {(!config || activeProfiles.length === 0) && (
        <small className="muted">
          {!config ? 'Connect to the host before creating schedules.' : 'Create or restore an active council member first.'}
        </small>
      )}
    </form>
  );

  return (
    <section className="scheduled-tasks-panel" aria-label="Scheduled tasks">
      <div className="scheduled-tasks-header">
        <div>
          <h3>Scheduled automations</h3>
          <small>Durable local cron jobs · Enforces member access policy &amp; cross-turn budgets</small>
        </div>
        <button
          type="button"
          className="icon-action-button primary"
          aria-label={showCreate && !editingSchedule ? 'Cancel' : 'New schedule'}
          title={showCreate && !editingSchedule ? 'Cancel' : 'New schedule'}
          onClick={() => {
            if (showCreate && !editingSchedule) {
              resetForm();
              setShowCreate(false);
            } else {
              resetForm();
              setShowCreate(true);
            }
          }}
          aria-expanded={showCreate && !editingSchedule}
        >
          <Icon name={showCreate && !editingSchedule ? 'close' : 'plus'} size={15} />
        </button>
      </div>

      {error && <p className="field-error" role="alert">{error}</p>}

      {showCreate && !editingSchedule && renderScheduleForm()}

      {loading ? (
        <p className="muted">Loading scheduled automations…</p>
      ) : schedules.length === 0 ? (
        <p className="muted">No scheduled automations yet. Create one to run Council Member tasks on a local schedule.</p>
      ) : (
        <ul className="schedule-list">
          {schedules.map(item => {
            if (editingSchedule?.id === item.id) {
              return (
                <li key={item.id}>
                  {renderScheduleForm()}
                </li>
              );
            }
            const owner = profiles.find(p => p.id === item.agentDefinitionId);
            const hasHistory = Boolean(item.lastRunStatus || item.lastRunUtc);
            return (
              <li key={item.id} className={`schedule-card ${selectedScheduleId === item.id ? 'selected' : ''}`}>
                <div className="schedule-card-top">
                  <div className="schedule-owner">
                    {owner && <Portrait identity={toOwnerIdentity(owner)} />}
                    <div>
                      <strong>{item.name}</strong>
                      <small>{item.agentName} · <code>{item.cronExpression}</code> · {item.misfirePolicy}</small>
                    </div>
                  </div>
                  <span className={`status-pill ${item.isPaused ? 'paused' : 'active'}`}>
                    {item.isPaused ? 'Paused' : 'Active'}
                  </span>
                </div>
                <p className="schedule-prompt-preview">{item.prompt}</p>
                <div className="schedule-meta">
                  <small>Next: {item.nextRunUtc ? formatUtcTimestamp(item.nextRunUtc) : '—'}</small>
                  {item.lastRunStatus && <small>Last: {item.lastRunStatus}</small>}
                </div>
                <div className="schedule-actions">
                  <button type="button" className="icon-action-button" aria-label={`Run ${item.name} now`} title="Run now" disabled={busyId === item.id} onClick={() => handleRunNow(item)}>
                    <Icon name="play" size={14} />
                  </button>
                  <button id={`schedule-edit-${item.id}`} type="button" className="icon-action-button" aria-label={`Edit ${item.name}`} title="Edit schedule" disabled={busyId === item.id} onClick={() => startEdit(item)}>
                    <Icon name="edit" size={14} />
                  </button>
                  <button type="button" className="icon-action-button" aria-label={item.isPaused ? `Resume ${item.name}` : `Pause ${item.name}`} title={item.isPaused ? 'Resume' : 'Pause'} disabled={busyId === item.id} onClick={() => handleTogglePause(item)}>
                    <Icon name={item.isPaused ? 'resume' : 'pause'} size={14} />
                  </button>
                  <button type="button" className="icon-action-button" aria-label={`History for ${item.name}`} title={hasHistory ? 'Run history' : 'No run history yet'} disabled={busyId === item.id || !hasHistory} aria-pressed={selectedScheduleId === item.id} onClick={() => void loadRuns(item.id)}>
                    <Icon name="clock" size={14} />
                  </button>
                  <span className="card-action-spacer" />
                  {confirmDeleteId === item.id ? (
                    <>
                      <button type="button" className="icon-action-button danger-button" disabled={busyId === item.id} aria-label={`Confirm delete ${item.name}`} title="Confirm delete" onClick={() => handleDelete(item)}>
                        <Icon name="check" size={14} />
                      </button>
                      <button type="button" className="icon-action-button" aria-label="Cancel delete" title="Cancel" onClick={() => {
                        setConfirmDeleteId(null);
                        requestAnimationFrame(() => document.getElementById(`schedule-delete-${item.id}`)?.focus());
                      }}><Icon name="close" size={14} /></button>
                    </>
                  ) : (
                    <button id={`schedule-delete-${item.id}`} type="button" className="icon-action-button danger-button" disabled={busyId === item.id} aria-label={`Delete ${item.name}`} title="Delete schedule" onClick={() => setConfirmDeleteId(item.id)}>
                      <Icon name="trash" size={14} />
                    </button>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}

      {selectedScheduleId && (
        <div className="schedule-history" aria-label="Schedule run history">
          <div className="schedule-card-top">
            <h4>Run history</h4>
            <button type="button" className="icon-button" aria-label="Close run history" title="Close run history" onClick={() => { streamAbort.current?.abort(); setStreamModalOpen(false); setStreamingRunId(null); setSelectedScheduleId(null); setRuns([]); }}><Icon name="close" size={16} /></button>
          </div>
          {runsLoading ? (
            <p className="muted">Loading runs…</p>
          ) : runs.length === 0 ? (
            <p className="muted">No runs recorded yet.</p>
          ) : (
            <ul className="schedule-run-list">
              {runs.map(run => (
                <li key={run.id} className="schedule-run-item">
                  <div className="schedule-run-head">
                    <span className={`status-pill ${run.status.toLowerCase()}`}>{run.status}</span>
                    <small>{run.triggerKind} · {formatUtcTimestamp(run.createdUtc)}</small>
                  </div>
                  {run.summaryText && <p className="schedule-run-summary">{run.summaryText}</p>}
                  {!run.summaryText && (run.errorCode || run.status === 'Failed') && (
                    <small className="field-error">
                      {run.errorCode ? `${run.errorCode}: ` : ''}{describeRunError(run.errorCode)}
                    </small>
                  )}
                  {run.summaryText && run.errorCode && (
                    <small className="field-error">Error code: {run.errorCode}</small>
                  )}
                  <div className="schedule-actions">
                    <button
                      type="button"
                      className="icon-action-button"
                      aria-label="Inspect live stream"
                      title={streamingRunId === run.id && streamModalOpen ? 'Streaming live output' : 'Inspect stream'}
                      aria-pressed={streamingRunId === run.id && streamModalOpen}
                      onClick={() => void attachRunStream(run.id, selectedScheduleId, run.errorCode, true)}
                    >
                      <Icon name="stream" size={14} />
                    </button>
                    {run.conversationId && onOpenConversation && (
                      <button
                        type="button"
                        className="icon-action-button"
                        aria-label="Open session"
                        title="Open session"
                        onClick={() => onOpenConversation(run.conversationId!)}
                      >
                        <Icon name="open" size={14} />
                      </button>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
      <TextEditorModal
        open={streamModalOpen && !!streamingRunId}
        readOnly
        liveStreaming={streamStatus === 'Running' || streamStatus === 'Queued' || streamStatus === 'Connecting…'}
        title="Live progress"
        subtitle={schedules.find(s => s.id === selectedScheduleId)?.name}
        statusBadge={streamStatus || undefined}
        value={streamText}
        error={streamError}
        onClose={() => {
          streamAbort.current?.abort();
          setStreamModalOpen(false);
          setStreamingRunId(null);
        }}
      />
    </section>
  );
}
