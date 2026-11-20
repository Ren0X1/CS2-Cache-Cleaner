using System.Diagnostics;
using System.IO;
using System.Reflection;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using DriveFile = Google.Apis.Drive.v3.Data.File;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// Sube los ZIP de userdata a una carpeta del Google Drive del usuario.
///
/// Permiso usado: drive.file (el minimo): el programa solo ve y toca los
/// archivos que crea el mismo, nunca el resto de tu Drive. La primera vez
/// se abre el navegador para iniciar sesion; el token (con refresh) queda
/// en %LOCALAPPDATA%\RNX Cache Cleaner\google y se renueva solo.
/// </summary>
public static class DriveSync
{
    private static readonly string[] Scopes = [DriveService.Scope.DriveFile];
    private const string UserKey = "user";
    private const string FolderMime = "application/vnd.google-apps.folder";

    /// <summary>El cliente OAuth viene embebido en el .exe (lo mete el workflow desde un secret).</summary>
    public static bool IsConfigured => Assembly.GetExecutingAssembly().GetManifestResourceInfo("google_client.json") is not null;

    private static ClientSecrets LoadSecrets()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("google_client.json")
                      ?? throw new InvalidOperationException("Esta compilacion no trae el cliente OAuth de Google (google_client.json).");
        return GoogleClientSecrets.FromStream(s).Secrets;
    }

    private static FileDataStore Store => new(AppPaths.GoogleToken, fullPath: true);

    public static async Task<bool> HasTokenAsync()
    {
        try { return await Store.GetAsync<TokenResponse>(UserKey) is not null; }
        catch { return false; }
    }

    /// <summary>
    /// Devuelve credenciales. Si no hay token y <paramref name="interactive"/> es true,
    /// abre el navegador para iniciar sesion; si es false (modo /todo desatendido) devuelve null.
    /// </summary>
    public static async Task<UserCredential?> AuthorizeAsync(bool interactive, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        if (!interactive && !await HasTokenAsync()) return null;
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(LoadSecrets(), Scopes, UserKey, ct, Store, new BrowserReceiver());
    }

    public static async Task<string?> GetAccountEmailAsync(CancellationToken ct)
    {
        var cred = await AuthorizeAsync(false, ct);
        if (cred is null) return null;
        using var svc = Service(cred);
        var req = svc.About.Get();
        req.Fields = "user(emailAddress,displayName)";
        var about = await req.ExecuteAsync(ct);
        return about.User?.EmailAddress ?? about.User?.DisplayName;
    }

    public static async Task SignOutAsync()
    {
        try
        {
            var cred = await AuthorizeAsync(false, CancellationToken.None);
            if (cred is not null) await cred.RevokeTokenAsync(CancellationToken.None);
        }
        catch { /* si falla el revoke, al menos borramos el token local */ }
        try { await Store.ClearAsync(); } catch { }
    }

    private static DriveService Service(UserCredential cred) => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = cred,
        ApplicationName = "RNX Cache Cleaner",
    });

    /// <summary>Sube los ZIP que hayan cambiado desde la ultima subida.</summary>
    public static async Task<bool> UploadAsync(Settings settings, IReadOnlyList<BackupZip> zips, bool interactive,
                                               IProgress<double>? progress, CancellationToken ct)
    {
        if (!IsConfigured) { Logger.Warn(T("lg.dr.noBuild")); return false; }
        if (zips.Count == 0) { Logger.Detail(T("lg.dr.noZip")); return true; }

        var cred = await AuthorizeAsync(interactive, ct);
        if (cred is null)
        {
            Logger.Warn(T("lg.dr.noLogin"));
            return false;
        }

        using var svc = Service(cred);
        var rootId = await EnsureFolderAsync(svc, settings.DriveFolder, "root", ct);
        Logger.Detail(T("lg.dr.folder", settings.DriveFolder));

        await MigrateLooseZipsAsync(svc, rootId, ct);

        // La carpeta de dia mas reciente es la copia "actual"
        var days = await ListDayFoldersAsync(svc, rootId, ct);
        var latest = days.FirstOrDefault();
        var latestFiles = latest is null ? [] : await ListZipsAsync(svc, latest.Id, ct);

        bool Same(DriveFile? f, BackupZip z) =>
            f?.AppProperties is { } ap && ap.TryGetValue("hash", out var h) && h == z.Hash;

        if (latest is not null && zips.All(z => Same(latestFiles.FirstOrDefault(f => f.Name == z.SteamId + ".zip"), z)))
        {
            Logger.Detail(T("lg.dr.upToDate", latest.Name));
            progress?.Report(1);
            return true;
        }

        // Hay cambios: se usa (o se crea) la carpeta de hoy
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var todayId = latest?.Name == today ? latest.Id : await EnsureFolderAsync(svc, today, rootId, ct);
        var todayFiles = todayId == latest?.Id ? latestFiles : await ListZipsAsync(svc, todayId, ct);
        Logger.Detail(T("lg.dr.day", today));

        long total = zips.Sum(z => z.Size), done = 0;
        bool ok = true;
        foreach (var z in zips)
        {
            ct.ThrowIfCancellationRequested();
            var name = z.SteamId + ".zip";
            var inToday = todayFiles.FirstOrDefault(f => f.Name == name);
            var inLatest = latestFiles.FirstOrDefault(f => f.Name == name);
            var props = new Dictionary<string, string> { ["steamid"] = z.SteamId, ["hash"] = z.Hash };

            if (Same(inToday, z))
            {
                Logger.Detail(T("lg.dr.fileOk", today + "/" + name));
            }
            else if (inToday is null && todayId != latest?.Id && Same(inLatest, z))
            {
                // No ha cambiado: se duplica dentro de Drive, sin volver a subirlo
                var copy = svc.Files.Copy(new DriveFile { Name = name, Parents = [todayId], AppProperties = props }, inLatest!.Id);
                copy.Fields = "id";
                await copy.ExecuteAsync(ct);
                Logger.Detail(T("lg.dr.copied", name, today));
            }
            else
            {
                await using var fs = File.OpenRead(z.Path);
                long baseDone = done;
                IUploadProgress result;
                if (inToday is null)
                {
                    var req = svc.Files.Create(new DriveFile { Name = name, Parents = [todayId], AppProperties = props,
                                                               Description = $"Backup userdata Steam {z.SteamId}" }, fs, "application/zip");
                    req.Fields = "id";
                    req.ProgressChanged += p => progress?.Report(total == 0 ? 1 : (double)(baseDone + p.BytesSent) / total);
                    result = await req.UploadAsync(ct);
                }
                else
                {
                    // Segundo backup del mismo dia: se actualiza el de hoy
                    var req = svc.Files.Update(new DriveFile { AppProperties = props }, inToday.Id, fs, "application/zip");
                    req.ProgressChanged += p => progress?.Report(total == 0 ? 1 : (double)(baseDone + p.BytesSent) / total);
                    result = await req.UploadAsync(ct);
                }
                if (result.Status == UploadStatus.Completed)
                {
                    settings.DriveUploaded[z.SteamId] = z.Hash;
                    settings.Save();
                    Logger.Ok(T("lg.dr.uploaded", today + "/" + name, Sys.FormatBytes(z.Size)));
                }
                else
                {
                    ok = false;
                    Logger.Error(T("lg.dr.upFail", today + "/" + name, result.Exception?.Message));
                }
            }
            done += z.Size;
            progress?.Report(total == 0 ? 1 : (double)done / total);
        }

        if (ok) await RotateAsync(svc, rootId, ct);
        progress?.Report(1);
        return ok;
    }

    /// <summary>Carpetas de dia que se guardan: la actual y la anterior.</summary>
    private const int KeepDays = 2;

    /// <summary>
    /// Deja solo las <see cref="KeepDays"/> carpetas de dia mas nuevas. Las sobrantes
    /// van a la papelera de Drive (recuperables 30 dias), no se borran del tiron.
    /// </summary>
    private static async Task RotateAsync(DriveService svc, string rootId, CancellationToken ct)
    {
        foreach (var old in (await ListDayFoldersAsync(svc, rootId, ct)).Skip(KeepDays))
        {
            try
            {
                await svc.Files.Update(new DriveFile { Trashed = true }, old.Id).ExecuteAsync(ct);
                Logger.Detail(T("lg.dr.trashed", old.Name));
            }
            catch (Exception ex) { Logger.Warn(T("lg.dr.trashFail", old.Name, ex.Message)); }
        }
    }

    /// <summary>
    /// Versiones anteriores dejaban los ZIP sueltos en la carpeta principal
    /// (SteamID.zip o SteamID_fecha.zip): se mueven a la carpeta del dia en que se crearon.
    /// </summary>
    private static async Task MigrateLooseZipsAsync(DriveService svc, string rootId, CancellationToken ct)
    {
        var loose = await ListZipsAsync(svc, rootId, ct);
        if (loose.Count == 0) return;
        var dayIds = new Dictionary<string, string>();
        // Del mas nuevo al mas antiguo: si hay varias del mismo dia y cuenta, la mas nueva se queda con el nombre
        foreach (var f in loose.OrderByDescending(f => f.CreatedTimeDateTimeOffset ?? DateTimeOffset.MinValue))
        {
            var day = (f.CreatedTimeDateTimeOffset ?? DateTimeOffset.Now).LocalDateTime.ToString("yyyy-MM-dd");
            if (!dayIds.TryGetValue(day, out var dayId))
                dayIds[day] = dayId = await EnsureFolderAsync(svc, day, rootId, ct);
            var sid = f.Name.Split('_', '.')[0];
            var exists = (await ListZipsAsync(svc, dayId, ct)).Any(x => x.Name == sid + ".zip");
            var upd = svc.Files.Update(new DriveFile { Name = exists ? f.Name : sid + ".zip" }, f.Id);
            upd.AddParents = dayId;
            upd.RemoveParents = rootId;
            await upd.ExecuteAsync(ct);
            Logger.Detail(T("lg.dr.moved", f.Name, day));
        }
    }

    /// <summary>Subcarpetas yyyy-MM-dd de la carpeta principal, de la mas nueva a la mas antigua.</summary>
    private static async Task<List<DriveFile>> ListDayFoldersAsync(DriveService svc, string rootId, CancellationToken ct)
    {
        var list = svc.Files.List();
        list.Q = $"'{rootId}' in parents and trashed=false and mimeType='{FolderMime}'";
        list.Fields = "files(id,name)";
        list.PageSize = 200;
        var r = await list.ExecuteAsync(ct);
        return r.Files.Where(f => DateTime.TryParseExact(f.Name, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _))
                      .OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList();
    }

    private static async Task<List<DriveFile>> ListZipsAsync(DriveService svc, string folderId, CancellationToken ct)
    {
        var list = svc.Files.List();
        list.Q = $"'{folderId}' in parents and trashed=false and mimeType!='{FolderMime}'";
        list.Fields = "files(id,name,createdTime,appProperties)";
        list.PageSize = 200;
        var r = await list.ExecuteAsync(ct);
        return r.Files.Where(f => f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static async Task<string> EnsureFolderAsync(DriveService svc, string name, string parentId, CancellationToken ct)
    {
        var list = svc.Files.List();
        list.Q = $"mimeType='{FolderMime}' and name='{Escape(name)}' and trashed=false and '{parentId}' in parents";
        list.Fields = "files(id)";
        var r = await list.ExecuteAsync(ct);
        if (r.Files.Count > 0) return r.Files[0].Id;

        var create = svc.Files.Create(new DriveFile { Name = name, MimeType = FolderMime, Parents = [parentId] });
        create.Fields = "id";
        return (await create.ExecuteAsync(ct)).Id;
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

    /// <summary>
    /// El programa corre como administrador; si abrimos el navegador directamente
    /// tambien correria elevado. Pasandoselo a explorer.exe se abre con el
    /// usuario normal, en el navegador que ya tengas abierto.
    /// </summary>
    private sealed class BrowserReceiver() : LocalServerCodeReceiver(ClosePage())
    {
        private static string ClosePage() =>
            "<html><head><meta charset='utf-8'><title>RNX Cache Cleaner</title></head>" +
            "<body style='background:#070a09;color:#dce4df;font-family:Segoe UI,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0'>" +
            "<div style='text-align:center;border:1px solid #2f5240;padding:40px 56px'>" +
            "<h1 style='color:#b86e00;margin:0 0 8px;font-weight:600'>" + T("web.title") + "</h1>" +
            "<p style='color:#8c9a92'>" + T("web.sub") + "</p></div></body></html>";

        protected override bool OpenBrowser(string url)
        {
            // OJO: no pasar la URL por explorer.exe: la recorta y Google responde
            // "Missing required parameter: redirect_uri". ShellExecute directo:
            // si el navegador ya esta abierto, la pestana se abre en el de siempre.
            Logger.Detail(T("lg.dr.browser", url.Contains("redirect_uri=") ? "OK" : "MISSING"));
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn(T("lg.dr.browserFail", ex.Message));
                return base.OpenBrowser(url);
            }
        }
    }
}
