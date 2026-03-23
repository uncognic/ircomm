# ircomm
A lightweight desktop IRC client built with WPF on .NET 8.
<p float="left">
  <img src="https://raw.githubusercontent.com/uncognic/ircomm/refs/heads/main/screenshot.png" width="1000" />
</p>

## Features
- Saved server profiles with add/edit/delete management.
- Direct one-off server connection dialog.
- Optional TLS/SSL connection support.
- Optional bouncer/PASS authentication fields (user/network/password).
- NickServ identify attempt after connect when configured.
- Channel list with quick join flow.
- Per-channel chat history and channel switching.
- Channel user list with live count.
- Slash commands in the input box:
  - `/join`
  - `/nick`
  - `/msg`
  - `/quit`
- Chat tools:
  - Export current chat view to `.txt`
  - Clear current chat view
  - Optional automatic chat append-to-file
- Link-aware messages:
  - Clickable URLs
  - Inline image preview for direct image links

## Files
The app writes runtime files in the application directory:

- `profiles.json`
- `settings.json`
- `chat.txt` (default auto-save file if enabled)

## Notes
- Profile and bouncer passwords are currently stored in plain text in `profiles.json`.

## License
GPLv3
