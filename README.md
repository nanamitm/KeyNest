# KeyNest

KeyNest is a password manager for Windows, built with WPF on .NET 10 and styled with the Windows 11 Fluent theme.
It is inspired by the classic Japanese freeware *ID Manager* and can import its XML export, so existing users can move their data over.

> **Note:** The user interface is currently Japanese only.

## Features

- **Encrypted vault** — everything (entries, settings, personal info) is stored in a single encrypted file.
- **Tree view** — nested groups and entries, search, and drag & drop to reorganize.
- **Flexible entries** — four general-purpose fields per entry whose labels and masking you can rename freely, plus URL, e-mail, file path, serial number, issue/expiration dates and notes. Entries close to (or past) their expiration date are flagged.
- **Auto-type** — named "paste types" (sequences such as *field 1 → Tab → field 2*) that type into the window you were using before switching to KeyNest. Text is sent as Unicode, so it is not affected by the keyboard layout or IME state.
- **Tray and global hotkey** — stay in the notification area and pop up a menu of entries anywhere to copy, paste or auto-type.
- **Clipboard hygiene** — copied values are cleared automatically after a configurable delay.
- **Password generator** — choose character classes, length (1–128) and whether to exclude look-alike characters.
- **Windows Hello unlock** — optional, in addition to the master password.
- **Auto-lock** — lock after a period of inactivity (in KeyNest or on the whole PC); unsaved changes are saved first.
- **Personal info** — names, addresses, phone numbers etc. for filling in forms, available from the tray menu and in paste types.
- **Import / export** — import *ID Manager* XML; export to CSV (UTF-8) or *ID Manager*-compatible XML.
- **Portable mode** — keep the app and its data together on a USB drive.

## Requirements

- Windows 10 version 1809 or later (Windows 11 recommended)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build
- Windows Hello (PIN, face or fingerprint) set up on the PC, if you want to use Hello unlock

## Build and run

```powershell
dotnet build
dotnet run --project src/KeyNest.App
dotnet test
```

To open a specific vault file, pass its path:

```powershell
dotnet run --project src/KeyNest.App -- D:\path\to\vault.knv
```

## Portable build

```powershell
pwsh -File tools/publish-portable.ps1 -Zip
```

This creates `publish/KeyNest-portable-win-x64/` containing a self-contained, single-file `KeyNest.exe` (no .NET installation needed on the target PC) and an empty `portable` marker file, plus a zip for distribution.
Options: `-Runtime win-arm64`, `-NoCompress`, `-Output <folder>`.
An existing `data` folder in the output directory is never deleted, overwritten or included in the zip.

## Where data is stored

| Mode | Vault | App settings |
| --- | --- | --- |
| Normal | `%APPDATA%\KeyNest\vault.knv` | `%APPDATA%\KeyNest\settings.json` |
| Portable (a file named `portable` next to `KeyNest.exe`) | `data\vault.knv` next to the exe | `data\settings.json` next to the exe |

`settings.json` holds only non-secret preferences that are needed before unlocking (tray, hotkey, window position, …).
Windows Hello enrollments are always kept on the local PC in `%LOCALAPPDATA%\KeyNest\hello`, because the Hello key is bound to that PC.

## Security design

- The vault is encrypted with **AES-256-GCM**. The key is derived from the master password with **PBKDF2-SHA256** (600,000 iterations, random 16-byte salt). The file header is authenticated as associated data, so tampering is detected.
- Files are written to a temporary file first and then swapped in, keeping one previous version as `.bak`. Changing the master password also re-encrypts the backup.
- **Windows Hello:** a TPM-backed Hello key signs a random challenge; a key derived from the signature (HKDF-SHA256) wraps the vault key with AES-GCM. The signature itself is never stored, so the enrollment file alone cannot unlock the vault. Changing the master password invalidates the enrollment.
- Exports are **not encrypted**. KeyNest asks for the master password before exporting and shows a warning.

KeyNest has not been independently audited. Use it at your own risk and keep backups of your vault.

## Migrating from ID Manager

Use ID Manager's **XML** export (its CSV export cannot represent the folder hierarchy), then click **取り込み** (Import) in KeyNest.
Folders, field labels and values, dates, paste types and (optionally) personal info are imported into a new group; existing data is left untouched.

## Project structure

```
src/KeyNest.Core     Vault model, encryption, storage, import/export, auto-type planning (UI-independent)
src/KeyNest.App      WPF application
tests/               xUnit tests for KeyNest.Core
tools/               Icon generator and portable publishing script
```

## Disclaimer

KeyNest is an independent project and is not affiliated with or endorsed by the authors of *ID Manager*.
The name is used only to describe file-format compatibility.

## License

[MIT](LICENSE)
