import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { RunTraceList } from './RunTraceList';

describe('RunTraceList', () => {
  it('expands tool arguments/results and dispatches approval decisions', () => {
    const onResolveDecision = vi.fn();
    render(
      <RunTraceList
        runId="run-1"
        onResolveDecision={onResolveDecision}
        events={[
          {
            id: 'ev-1',
            sequenceNumber: 1,
            eventType: 'tool.completed',
            serverId: 'filesystem-mcp',
            toolName: 'fs_read_file',
            securityTier: 'Tier1',
            status: 'Completed',
            argumentsRedactedJson: '{"path":"README.md"}',
            resultRedactedJson: '1: # Guildhall',
            durationMs: 12,
          },
          {
            id: 'ev-2',
            sequenceNumber: 2,
            eventType: 'approval.requested',
            serverId: 'github-mcp',
            toolName: 'github_create_issue',
            securityTier: 'Tier3',
            status: 'PendingApproval',
            argumentsRedactedJson: '{"title":"New issue"}',
          },
        ]}
      />,
    );

    const toggle = screen.getByRole('button', { name: /filesystem-mcp · fs_read_file/i });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByText('1: # Guildhall')).toBeInTheDocument();

    const approvalRegion = screen.getByRole('region', { name: /Approval required for github_create_issue/i });
    expect(approvalRegion).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Approve' }));
    expect(onResolveDecision).toHaveBeenCalledWith('run-1', 'ev-2', true);
  });
});
