# Phase 1 frontend polish — November 16, 2026

Owner request: improve council/session hierarchy, response feedback, queue density and desktop layout; add member deletion as well as archival. Implementation belongs to `G:/repos/guildhall-frontend`, branch `codex/frontend`.

## Roadmap and boundaries

Master source: `C:/Users/isagn/Downloads/personal_ai_ecosystem_master_plan.md` v1.1, referenced in `docs/architecture-review.md`. This batch advances Phase 1 chat/council usability under `docs/phase-1.md` and the owner's amendments in `docs/product-roadmap-amendments.md`. It does not implement tools, schedulers, cloud providers or autonomous activity. Starter suggestions are deterministic local examples based on member role/instructions, editable before sending. Loading words are cosmetic waiting feedback, never claims of tool work.

## Implementation sequence and ownership

1. Luna API support: ChatContracts/ChatEndpoints and isolated SQL API tests. Expose persisted terminal reply timestamp for unread markers, truthful elapsed timing, and promote an existing queued run without resubmission. No live database upgrade, new runtime grants or shared process restart.
2. Luna chat interactions: App/api types, focused React helpers and frontend tests. Implement unread/read contrast (request 1), tailored starters (4), portrait-free session rows (5), timing blip (6), waiting wording (8), compact queue with promote/remove (9), and remove Open chat (13). Use clock/shield icons supplied by layout task (10/11).
3. Luna layout: style.css/Icon only. Correct clipped paperclip (2), root overflow/overscroll (3), grouping connector lines (5), clock/shield (10/11), and responsive composer/queue/sidebar behavior (12).
4. Integrate and verify: unit/API tests, production build and browser fixtures for navigation, unread transitions, waiting/streaming/terminal states, queued attachments/promote/remove, and normal/narrow/zoomed desktop layouts. Independent project UI reviewer inspects resulting renders before presentation.

Owner resolved member deletion (7): permanently delete the member and ALL linked sessions/messages/attachments after explicit confirmation; archives still retain sessions. API must refuse active/queued work, enforce revision checks, delete transactionally, and preserve unrelated members/history. Prepare the narrowly scoped AgentDefinitions DELETE permission for concrete approval before applying to GuildhallDev.

## Acceptance and verification

- Green unread marker only represents a newer unseen terminal reply; opening/reading clears it. Read session titles are quieter. Inactive session completions are refreshed without a permanent high-frequency background loop. Read markers persist only local IDs/timestamps, not prompts/content.
- Member portraits appear once in council headings. Nested sessions have subtle connectors and distinguishable keyboard/selected states.
- Suggestion buttons change the draft and focus composer, never submit automatically or overwrite an existing draft without care. No model call generates suggestions.
- Elapsed timing uses server records, labels its measurement honestly, survives reopen and omits unavailable data. Queue waiting is not presented as measured inference.
- Only empty active replies animate waiting wording; reduced motion disables transitions. No invented progress/work claims or unnecessary live announcements.
- Queued user/assistant transcript placeholders collapse to composer-adjacent rows with snippets and named promote/remove controls. Promote reuses the same run and attachments; stale/active errors remain actionable.
- No root horizontal scrollbar or scrolling past the application at widths 420/620/900/1280/1920 and scales 80/100/125/150 percent; long code blocks retain their own horizontal scrolling. Composer controls remain usable with side panels, attachments, draft text and queued work.
- Existing archive, restore, stop, message send, export and profile editing remain functional.

Agent-run commands (planned until recorded as passed):

```powershell
npm.cmd --prefix apps/desktop run typecheck
npm.cmd --prefix apps/desktop run build
npm.cmd --prefix apps/desktop run test -- --run
dotnet build Guildhall.slnx -c Release
dotnet test tests/Guildhall.AgentHost.Tests -c Release --filter 'Category!=SqlIntegration'
$env:GUILDHALL_TEST_SQL_CONNECTION = 'Server=localhost;Database=master;Integrated Security=true;Encrypt=true;TrustServerCertificate=true'
dotnet test tests/Guildhall.AgentHost.Tests -c Release --filter 'Category=SqlIntegration'
```

SQL fixtures use only owned isolated databases and deterministic providers. Browser fixtures use project contracts and fake endpoints; native/live QC remains distinct. The prior main dev launcher reports exited processes, so no shared app is assumed available. Do not interpret stale readiness as current live verification.

## Verification record and owner functional QC

Implemented on `codex/frontend` by three GPT-6 Luna agents (API, chat interactions, layout/icons), followed by root integration fixes and the independent project UI reviewer. Backend Release build, 33 deterministic tests and 2 isolated SQL tests passed. Frontend typecheck/production build and 33 tests passed. Migration preview for `20261116134707_CouncilMemberDeletionPermission` ran without applying changes. Its sole new runtime grant is DELETE on AgentDefinitions; existing Conversations DELETE and dependent-table restrictions remain unchanged. No live database fixture, inference, app restart, merge, or push was performed.

The UI reviewer identified and integration corrected menu keyboard reachability, duplicate saved/unread dots, and narrow collapsed-sidebar column placement. Browser fixture results and final reviewer status are recorded below after completion. Native WebView/live GuildhallDev interaction is separate and remains unverified for this batch.

The owner approved the [permission upgrade](council-member-deletion-permission-approval.md), and it is applied to GuildhallDev. After the coordinator integrates this branch into the main checkout, build and launch using the main checkout with its existing protected runtime credential:

```powershell
Set-Location G:\repos\guildhall
npm.cmd --prefix apps/desktop run tauri -- build --debug --no-bundle
pwsh -NoProfile -File .\scripts\Start-Dev.ps1 -BuiltDesktop
```

Close any existing development app through its owner before relaunching; the launch script refuses occupied ports. Do not copy runtime credentials into tracked files or another worktree.

1. Select a Developer and then a Jester/Image member. New-session suggestion buttons should differ, fill an editable draft, and focus the composer without sending. Clear the draft between members.
2. Finish a reply in one session while viewing another. The unseen response gets one green dot and stronger title. Open that session: it becomes read and quieter. Member portraits stay on headings; session rows use small chat icons and connector lines.
3. Check the response-time blip above a completed reply; it includes queue wait. While awaiting first text, short waiting phrases cycle without pretending tool work. Reduced-motion mode shows a static phrase.
4. Queue two follow-ups. Only compact composer-adjacent rows appear for pending turns. Left icons promote/remove the existing queued message, with no duplicate user turn. Attachment access is available from the compact files disclosure. An error leaves saved content intact.
5. Archive and restore a member, confirming sessions are retained. For destructive QC use a deliberately disposable member/session only: Cancel member deletion once, then confirm its irreversible member/session/message/attachment removal. Other members remain. Never delete valuable history just to test.
6. Resize through 420/620/900/1280/1920px and change display scale through 80/100/125/150%. Check both collapsed and expanded sidebar, member/tools panels, long draft, attachment and queued messages. The composer remains reachable, paperclip is intact, and root page has no scrollbar/overscroll; code blocks may scroll internally. Clock and shield navigation retain accurate labels. Session tools remains visible and Open chat is absent.
7. Tab to member actions, use arrows/Home/End across Archive and Delete, Enter to open confirmation, Escape to cancel, and confirm focus returns. Confirm destructive controls are unavailable or refused while replies are active/queued.

### Final browser and independent review results

The Playwright fixture passed 20 width/scale combinations (420/620/900/1280/1920 × 80/100/125/150%), plus explicit collapsed sidebar/composer/tools at 420×150%, member panel at 420×100/150%, queue hit targets, independent codeblock scrolling, and narrow delete-dialog geometry. No browser JavaScript errors occurred. Final fixes also removed the obsolete narrow first-child action-hiding rule and put queue promote/remove controls visually to the LEFT of snippets. Screenshots and `geometry.json` are in ignored `apps/desktop/.local/ui-review`; reproducible local fixture is `apps/desktop/.local/responsive-review.py`. The isolated Vite listener on 1421 was stopped after testing.

Independent `.cursor/agents/guildhall-ui-reviewer.md` review reports no remaining blocking findings after inspecting normal, narrow and high-zoom renders, member/tools panels, queued rows and deletion modal. It independently tested field/tab/close reachability and deletion Escape focus return. Its separate extra short-window modal probe did not complete due to fixture setup interception; no pass is claimed for that particular probe. The layout agent's narrow dialog geometry check passed separately. All of this is deterministic browser fixture evidence, not native/live-host verification.

Final frontend regression run: 33 tests passed, production build/typecheck passed, and `git diff --check` passed. Backend final run: Release build zero warnings/errors, 33 deterministic tests and 2 isolated SQL integration tests passed. The permission migration was subsequently approved/applied; see its verification record. Changes are prepared on `codex/frontend` for pull-request review; native QC and merging remain outstanding.

PR preparation also added a SQL regression for promoting past a removed queued turn: promotion reuses only queued turn-number slots and leaves cancelled history unchanged.

## Resizable chat panes follow-up

The council sidebar, Session Tools panel, and Guild Member editor can each be resized on the main chat screen. Drag the divider with a mouse or touch; keyboard users can focus it and use arrows (10 px), Shift+arrows (40 px), Home (minimum), and End (maximum). Widths persist in local browser storage. At narrow widths, side panels remain overlays and the council collapses on entry to the overlay layout. The composer reflows its controls when a side panel leaves less than 680 px for chat.

Functional QC: on a desktop window, drag each divider in both directions and confirm the chat stays usable; resize at a narrow width and verify the overlays do not create horizontal page scrolling. Tab to each divider and use arrows/Home/End; confirm its focus outline and width value update. Reload and confirm the three saved widths return.

Automated verification for this follow-up: `npm.cmd --prefix apps/desktop run test -- --run` (43 passed), `npm.cmd --prefix apps/desktop run typecheck`, `npm.cmd --prefix apps/desktop run build`, and `git diff --check` passed. An isolated Playwright fixture with mocked API responses dragged all three dividers, confirmed saved widths after reload, and verified the 620 px member overlay has no horizontal overflow. At 1600 px with Session Tools open, the composer reflowed and its text area remained at least 250 px wide. The independent Luna UI reviewer inspected the rendered screenshots, confirmed the composer correction, and reported no remaining actionable findings. Screenshots and the repeatable local fixture are in ignored `apps/desktop/.local/pane-review`. Run the functional QC in the native window after integration; no native WebView verification was performed.
