# Chalk Test Playbook

## Test Matrix

| Scenario | Role | Steps | Expected Result |
| --- | --- | --- | --- |
| Create room | Host | Launch app, tap **Create Room**, press **Create**, wait for code, tap **Enter** | Room code replaced with server code, loading page appears, AR session activates when second device joins |
| Join room | Guest | Launch app, tap **Join Room**, enter host code, tap **Join** | Loading screen displays progress, transitions into AR session when connection completes |
| Undo line | Any | Draw at least two lines, tap **Undo** | Most recent local line removed locally and on remote device |
| Clear board | Any | Draw multiple lines, tap **Clear** | All drawings removed locally and remotely |
| Leave room | Any | Tap **Leave** on line-draw UI | Session tears down, XR turns off, main menu returns |
| Reconnect | Host or Guest | During loading screen disable network (airplane mode) then re-enable | Loading text updates with retry messaging, connection resumes or user returns to lobby on failure |
| UI blocking | Any | Open AR UI, tap sliders/buttons repeatedly | No accidental drawing while interacting with UI (verify by checking scene) |
| Error handling | Any | Enter invalid room code or shut down signaling server | Loading screen cancels and user returned to lobby with log message |

## Detailed Steps

1. **Create & Share Room**
   - From lobby tap **Create Room**.
   - Press **Create** and wait for real code from server (placeholder reads “Requesting…”).
   - Confirm **Enter** transitions to loading page and eventually into AR session.

2. **Join Existing Room**
   - On another device tap **Join Room**.
   - Enter provided code and press **Join**.
   - Observe loading UI status changes (“Joining…”, “Finalizing connection…”).

3. **Drawing Sync**
   - Draw strokes on host. Guest should see same strokes in sequence.
   - Tap **Undo** on host, verify removal on both peers.
   - Tap **Clear** on guest, verify board cleared for host as well.

4. **UI Interaction Safety**
   - While in AR session, drag sliders and color radios. Ensure no stray lines appear underneath.
   - Open any overlaying UI (Unity UI Toolkit docs) and confirm touches do not start drawing.

5. **Leave / Return Flow**
   - Tap **Leave**. XR rig and AR UI disable, lobby reappears.
   - Re-enter Create/Join flows to ensure repeated sessions work.

6. **Lifecycle**
   - Background the app (home button) while connecting. When returning, lobby should reset and sockets reconnect cleanly.
   - Quit the app; ensure no orphaned AR subsystems remain active on relaunch.

7. **Remote Video**
   - If a RawImage/Mesh is assigned to `RemoteVideoDisplay`, verify remote camera feed becomes visible after connection.

Document any regressions or unexpected logs when completing the matrix.
