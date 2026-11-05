# Local text-capture diagnostics (experimental)

This is a **display-only client diagnostic**, not a translator. It does not change
server protocol, gameplay, chat/input values, localization source, or public text
getters. No translation API, network transmission, automatic file export, or per-frame I/O
is added. Captured source strings are held in this process's bounded memory unless you explicitly export them.

## Enable / disable

Set `ROBUST_TEXT_CAPTURE=1` **before starting this custom engine client**. Restart
to apply changes. The default (unset, empty, `0`, or any unrecognized value) is OFF.
Each captured string displays as `#测试1 Original text`, etc. If the installed UI
font lacks Chinese glyphs, use `ROBUST_TEXT_CAPTURE=ascii` for `#test1 Original text`.
Font fallback is explicit, not automatic. Clear the variable and restart to disable.
The launcher/child process must inherit the variable and actually load this build;
setting it does not replace a launcher-downloaded stock engine.

PowerShell, for an already configured local custom-client launch:

```powershell
$env:ROBUST_TEXT_CAPTURE = 'ascii' # or '1' for #测试
# Run your existing custom-client launch command in this same shell.
# After closing the client:
Remove-Item Env:ROBUST_TEXT_CAPTURE
```

POSIX shells: prefix the existing custom-client launch command with
`ROBUST_TEXT_CAPTURE=ascii` (or `ROBUST_TEXT_CAPTURE=1`).

## Scope and IDs

- `Label.Text` and `Label.TextMemory`: the display copy is made on assignment;
  measurement and rendering use the same copy. Public getters retain original data.
- `RichTextLabel` and `OutputPanel`: literal rich-text nodes receive markers;
  formatting tags/attributes and embedded-control indices remain intact. Original
  messages/getters stay unchanged. Multiple literal nodes can mean multiple IDs in
  one visible sentence; equal literal text across widgets gets the same ID.
- Matching is exact ordinal text, including case and whitespace. IDs are assigned
  in first-capture order and are stable only within one process, not between runs.
- Empty/whitespace-only text is not captured. Maximum 10,000 distinct strings,
  16,384 UTF-16 characters per string, 2,000,000 source characters in total.
  Oversized/new strings beyond a limit remain unmarked; existing IDs are not evicted.
- A marker means text reached a supported display assignment, **not** proof the
  widget was visible. Hidden widgets may consume IDs. Source text can include chat
  or other private content; do not share screenshots/memory dumps without review.

Not covered: custom DrawString/game-world rendering, editable input boxes, text
produced by rich-tag handlers, or text baked into images. Markers can affect wrapping
and be clipped in fixed-size UI; this is intentionally diagnostic, not production UI.
Mutable TextMemory backing buffers must be reassigned after changes as required by
Label's existing contract. Rich messages should likewise be reassigned to refresh
an enabled diagnostic snapshot. No online server or authentication test is implied.

## Optional local mapping export

**Disconnect from the server first**, keeping the client process open (IDs stay
in memory). In the **client's local debug console**, run:

```text
text_capture_export "C:\Users\YOUR_NAME\Desktop\text-capture.json"
```

Use your own existing local directory (on Linux, an absolute path such as
`/home/your-name/text-capture.json`). The command is unavailable while connected, preventing a server-origin console
command from triggering export. It requires enabled capture and
creates a new file, refusing to overwrite existing files. It writes a bounded
snapshot with `Id`, original `Text`, and marked `Display`; it performs no upload.
UNC paths are rejected; use a local disk, not a mapped network/synced folder.
The file can include private chat and names: review before sharing and delete
when no longer needed. Export is never automatic and files are not build assets.
Export feedback can itself receive an ID after the snapshot was taken.

## Verify

Run the `TextCaptureDiagnosticsTest` NUnit fixture. Then launch locally once with
no variable and once with `ascii`: compare original label getters, repeated labels,
rich colors/embedded controls, long wrapped output, and clipping. Confirm no marker
enters an editable input or outgoing message. Live UI/server checks require a
working client runtime and are separate from unit tests.
