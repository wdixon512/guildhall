import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ScheduledTasksPanel } from './ScheduledTasksPanel';
import * as api from './api';

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api');
  return {
    ...actual,
    requestJson: vi.fn(),
  };
});

const mockConfig: api.Bootstrap = { hostUrl: 'http://127.0.0.1:43120', token: 'a'.repeat(32) };
const mockProfiles: api.AgentProfile[] = [
  { id: 'agent-1', name: 'Maren', role: 'Steward', portraitId: 0, accent: '#71465f', instructions: 'Rules', revision: 1, isArchived: false },
];

describe('ScheduledTasksPanel', () => {
  it('loads schedules, creates a new schedule, and toggles pause', async () => {
    const schedules: api.ScheduledAutomationView[] = [
      {
        id: 'sched-1',
        name: 'Morning brief',
        agentDefinitionId: 'agent-1',
        agentName: 'Maren',
        prompt: 'Summarize inbox',
        cronExpression: '@daily',
        timezoneId: 'Local',
        misfirePolicy: 'RunOnce',
        maxCatchUpRuns: 3,
        maxDurationSeconds: 300,
        isPaused: false,
        lastRunStatus: 'Failed',
        createdUtc: '2026-11-17T10:00:00Z',
        updatedUtc: '2026-11-17T10:00:00Z',
        revision: 1,
      },
    ];

    let createdPayload: { cronExpression?: string } = {};
    vi.mocked(api.requestJson).mockImplementation(async (_cfg, path, method, _signal, body) => {
      if (path === '/schedules' && method === 'GET') return schedules;
      if (path === '/schedules/sched-1' && method === 'PATCH') {
        schedules[0] = { ...schedules[0], isPaused: true, revision: 2 };
        return schedules[0];
      }
      if (path === '/schedules' && method === 'POST') {
        createdPayload = (body ?? {}) as { cronExpression?: string };
        const created: api.ScheduledAutomationView = {
          ...schedules[0],
          id: 'sched-2',
          name: 'Weekly check',
          cronExpression: String(createdPayload.cronExpression ?? ''),
        };
        schedules.push(created);
        return created;
      }
      return [];
    });

    render(<ScheduledTasksPanel config={mockConfig} profiles={mockProfiles} projects={[]} />);
    expect(await screen.findByText('Morning brief')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Pause Morning brief/i }));
    await waitFor(() => expect(screen.getByRole('button', { name: /Resume Morning brief/i })).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'New schedule' }));
    fireEvent.change(screen.getByPlaceholderText('Daily repo summary'), { target: { value: 'Weekly check' } });
    fireEvent.change(screen.getByLabelText('Schedule'), { target: { value: 'weekly' } });
    fireEvent.change(screen.getByLabelText('Time of day'), { target: { value: '14:30' } });
    fireEvent.click(screen.getByRole('button', { name: 'Fri' }));
    fireEvent.change(screen.getByPlaceholderText(/Review recent commits/), { target: { value: 'Check build' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create schedule' }));

    expect(await screen.findByText('Weekly check')).toBeInTheDocument();
    expect(createdPayload?.cronExpression).toBe('30 14 * * 1,5');
  });

  it('edits an existing schedule and surfaces failed run reasons', async () => {
    const schedules: api.ScheduledAutomationView[] = [
      {
        id: 'sched-1',
        name: 'Morning brief',
        agentDefinitionId: 'agent-1',
        agentName: 'Maren',
        prompt: 'Summarize inbox',
        cronExpression: '0 9 * * 3',
        timezoneId: 'Local',
        misfirePolicy: 'RunOnce',
        maxCatchUpRuns: 3,
        maxDurationSeconds: 300,
        isPaused: false,
        lastRunStatus: 'Failed',
        createdUtc: '2026-11-17T10:00:00Z',
        updatedUtc: '2026-11-17T10:00:00Z',
        revision: 1,
      },
    ];
    let patchBody: Record<string, unknown> = {};
    vi.mocked(api.requestJson).mockImplementation(async (_cfg, path, method, _signal, body) => {
      if (path === '/schedules' && method === 'GET') return schedules;
      if (path === '/schedules/sched-1' && method === 'PATCH') {
        patchBody = (body ?? {}) as Record<string, unknown>;
        schedules[0] = { ...schedules[0], name: String(patchBody.name ?? schedules[0].name), revision: 2 };
        return schedules[0];
      }
      if (path === '/schedules/sched-1/runs' && method === 'GET') {
        return [
          {
            id: 'run-1',
            scheduledAutomationId: 'sched-1',
            agentDefinitionId: 'agent-1',
            triggerKind: 'Manual',
            scheduledForUtc: '2026-11-17T12:00:00Z',
            status: 'Failed',
            attemptCount: 1,
            errorCode: 'model_missing',
            createdUtc: '2026-11-17T12:00:00Z',
          },
        ];
      }
      return [];
    });

    render(<ScheduledTasksPanel config={mockConfig} profiles={mockProfiles} projects={[]} />);
    expect(await screen.findByText('Morning brief')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Edit Morning brief/i }));
    expect(screen.queryByRole('button', { name: /Run Morning brief now/i })).toBeNull();
    expect(screen.getByPlaceholderText('Daily repo summary')).toHaveValue('Morning brief');
    fireEvent.change(screen.getByPlaceholderText('Daily repo summary'), { target: { value: 'Updated brief' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save schedule' }));

    expect(await screen.findByText('Updated brief')).toBeInTheDocument();
    expect(patchBody).toMatchObject({ expectedRevision: 1, name: 'Updated brief', cronExpression: '0 9 * * 3' });

    fireEvent.click(screen.getByRole('button', { name: /History for Updated brief/i }));
    expect(await screen.findByText(/No model is configured for this council member/i)).toBeInTheDocument();
  });
});
