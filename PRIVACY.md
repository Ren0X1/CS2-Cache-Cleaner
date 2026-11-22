# Privacy Policy — RNX Cache Cleaner

_Last updated: November 22, 2026_

RNX Cache Cleaner ("the app") is a free Windows tool made by Ren0X1 for cleaning caches and optimizing CS2. This page explains what the app does with your data, including the optional Google Drive backup.

## Google Drive backup (optional)

The backup is **off by default**. If you turn it on and sign in with Google:

- The app asks only for the **`drive.file`** permission. With it, the app can **only see and manage the files it creates itself** — your other Google Drive files are never visible to it.
- It uploads ZIP copies of your Steam configuration (`Steam\userdata`) to a folder in **your own** Google Drive (by default *RNX Cache Cleaner Backups*), and deletes old copies made by the app (it keeps the last two days).
- The files go **directly from your PC to your Google Drive**. They are never sent to the developer or to any other server.
- The sign-in token is stored **only on your PC** in `%LOCALAPPDATA%\RNX Cache Cleaner\google`.

You can disconnect at any time from the app (**Backup → Disconnect**) or from your Google account at <https://myaccount.google.com/permissions>.

## Everything else

- The app runs **locally**. It does not collect analytics, telemetry or personal data, and it has no server of its own.
- Its settings, log and local backups stay in `%LOCALAPPDATA%\RNX Cache Cleaner` on your PC.
- Network requests it makes: GitHub (to check for and download updates), Valve's public Steam API (the list of CS2 server locations) and, only if you enable the backup, Google (sign-in and Drive).

## Google API Services User Data Policy

The app's use of information received from Google APIs adheres to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including the Limited Use requirements. Google user data is used only to provide the backup feature to you and is never transferred, sold or used for advertising.

## Contact

Questions: open an issue at <https://github.com/Ren0X1/CS2-Cache-Cleaner/issues> or contact **ren0x1** on Discord.

---

### Resumen en español

La copia de seguridad en Google Drive es opcional y está desactivada por defecto. Solo usa el permiso `drive.file`: la app únicamente ve los archivos que crea ella misma, sube los ZIP de tu configuración de Steam a **tu propio** Drive y nada pasa por servidores del desarrollador. El token de sesión se queda en tu PC. Puedes desconectarla desde la app o en <https://myaccount.google.com/permissions>. La app no recoge datos ni estadísticas.
