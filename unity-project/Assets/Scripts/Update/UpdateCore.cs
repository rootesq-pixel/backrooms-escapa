using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

// v7.5.0: lógica pura del auto-actualizador (sin red ni UI, para poder probarla en SmokeTest):
//   versiones («v7.5.0» → 7.5.0) y su comparación, lectura del JSON de «releases/latest» de GitHub, elección del zip de Windows y
//   de su SHA-256, lista blanca de direcciones de descarga, comprobación y extracción segura del zip (sin rutas fuera de la carpeta)
//   y el pequeño script de PowerShell que sustituye los archivos cuando el juego se cierra (con vuelta atrás si algo falla).
public static class UpdateCore
{
    public const string Owner = "rootesq-pixel", Repo = "backrooms-escapa";
    public const string LatestApi = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";
    public const string ReleasesPage = "https://github.com/" + Owner + "/" + Repo + "/releases/latest";
    public const string ExeName = "BackroomsEscape.exe", DataDir = "BackroomsEscape_Data";
    public const string WorkDirName = "_update", ResultFile = "_update_result.txt", LogFile = "_update.log";
    // a dónde redirige GitHub las descargas de los archivos de una versión
    static readonly string[] AssetHosts = { "objects.githubusercontent.com", "release-assets.githubusercontent.com", "github-releases.githubusercontent.com" };

    // ---------------- versiones ----------------
    // «v7.5.0», «7.5», «V7.5.0-beta» → {7,5,0}; null si no es una versión
    public static int[] ParseVersion(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        s = s.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);
        Match m = Regex.Match(s, @"^(\d{1,6})(?:\.(\d{1,6}))?(?:\.(\d{1,6}))?(?:\.(\d{1,6}))?(?:$|[-+ ])");
        if (!m.Success) return null;
        int[] v = new int[4];
        for (int i = 0; i < 4; i++) v[i] = m.Groups[i + 1].Success ? int.Parse(m.Groups[i + 1].Value) : 0;
        return v;
    }
    // <0 si a es anterior a b, 0 si es la misma, >0 si es posterior; las versiones ilegibles cuentan como la más antigua
    public static int Compare(string a, string b)
    {
        int[] x = ParseVersion(a), y = ParseVersion(b);
        if (x == null || y == null) return (x == null ? 0 : 1) - (y == null ? 0 : 1);
        for (int i = 0; i < 4; i++) if (x[i] != y[i]) return x[i] < y[i] ? -1 : 1;
        return 0;
    }
    public static bool IsNewer(string remoteTag, string current) { return ParseVersion(remoteTag) != null && Compare(remoteTag, current) > 0; }
    public static string Clean(string tag) { int[] v = ParseVersion(tag); if (v == null) return tag; return v[0] + "." + v[1] + "." + v[2] + (v[3] != 0 ? "." + v[3] : ""); }

    // ---------------- JSON de GitHub ----------------
    [Serializable] public class ApiAsset { public string name; public string browser_download_url; public long size; public string digest; public string state; }
    [Serializable] public class ApiRelease { public string tag_name; public string name; public string body; public string html_url; public bool draft; public bool prerelease; public ApiAsset[] assets; }

    public class Release
    {
        public string tag, version, title, body, page;
        public string zipName, zipUrl; public long zipSize;
        public string sha256;          // de «digest» del archivo, de un archivo .sha256 descargado aparte o del texto de la versión
        public string shaUrl;          // archivo «<zip>.sha256», si existe y no había otro hash
    }

    // null si el JSON no es una versión publicada utilizable (sin etiqueta, borrador, prerelease o sin zip de Windows)
    public static Release ParseRelease(string json, out string why)
    {
        why = null; ApiRelease r;
        try { r = JsonUtility.FromJson<ApiRelease>(json); } catch (Exception e) { why = "JSON ilegible: " + e.Message; return null; }
        if (r == null || string.IsNullOrEmpty(r.tag_name)) { why = "sin etiqueta"; return null; }
        if (r.draft || r.prerelease) { why = "borrador o prerelease"; return null; }
        if (ParseVersion(r.tag_name) == null) { why = "etiqueta no es una versión: " + r.tag_name; return null; }
        Release o = new Release { tag = r.tag_name, version = Clean(r.tag_name), title = r.name ?? "", body = r.body ?? "", page = r.html_url };
        ApiAsset zip = null, sha = null;
        if (r.assets != null)
        {
            foreach (ApiAsset a in r.assets)
                if (a != null && a.name != null && a.name.EndsWith("_Windows.zip", StringComparison.OrdinalIgnoreCase) && (a.state == null || a.state == "uploaded")) { zip = a; break; }
            if (zip != null)
                foreach (ApiAsset a in r.assets)
                    if (a != null && a.name != null && string.Equals(a.name, zip.name + ".sha256", StringComparison.OrdinalIgnoreCase)) sha = a;
        }
        if (zip == null) { why = "la versión no tiene zip de Windows"; return null; }
        o.zipName = zip.name; o.zipUrl = zip.browser_download_url; o.zipSize = zip.size;
        o.sha256 = DigestSha(zip.digest) ?? ShaFromText(o.body);
        if (o.sha256 == null && sha != null) o.shaUrl = sha.browser_download_url;
        return o;
    }
    static string DigestSha(string d)
    {
        if (string.IsNullOrEmpty(d) || !d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
        string h = d.Substring(7).Trim().ToLowerInvariant(); return IsHex64(h) ? h : null;
    }
    // «SHA-256 del zip: `…64 hex…`» en las notas de la versión, o el contenido de un archivo .sha256 («hash  nombre»)
    public static string ShaFromText(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        Match m = Regex.Match(text, @"SHA-?256[^\n]{0,40}?(?<![0-9a-fA-F])([0-9a-fA-F]{64})(?![0-9a-fA-F])", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
        m = Regex.Match(text.Trim(), @"^([0-9a-fA-F]{64})(?![0-9a-fA-F])");
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }
    static bool IsHex64(string h) { return h != null && Regex.IsMatch(h, "^[0-9a-f]{64}$"); }

    // ---------------- direcciones permitidas ----------------
    // Solo HTTPS: el zip en github.com/<este repo>/releases/download/… y las redirecciones de GitHub a sus servidores de archivos.
    // allowLoopback: solo para la prueba de extremo a extremo con un servidor falso en 127.0.0.1 («-updatefeed»).
    public static bool IsAllowedUrl(string url, bool first, bool allowLoopback)
    {
        Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
        if (allowLoopback && (u.Scheme == "http" || u.Scheme == "https") && u.IsLoopback) return true;
        if (u.Scheme != "https" || !u.IsDefaultPort || !string.IsNullOrEmpty(u.UserInfo)) return false;
        string h = u.Host.ToLowerInvariant();
        if (h == "github.com") return u.AbsolutePath.StartsWith("/" + Owner + "/" + Repo + "/releases/download/", StringComparison.Ordinal);
        if (first) return false;   // el enlace inicial siempre es el de github.com
        return Array.IndexOf(AssetHosts, h) >= 0;
    }
    public static bool IsAllowedFeed(string url, bool allowLoopback)
    {
        Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
        if (allowLoopback && (u.Scheme == "http" || u.Scheme == "https") && u.IsLoopback) return true;
        return url == LatestApi;
    }

    // ---------------- archivos ----------------
    public static string FileSha256(string path)
    {
        using (SHA256 h = SHA256.Create()) using (FileStream f = File.OpenRead(path))
        {
            byte[] d = h.ComputeHash(f); StringBuilder sb = new StringBuilder(64);
            foreach (byte b in d) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    // Comprueba el zip sin extraerlo: entradas con rutas seguras, el ejecutable y su carpeta de datos, nada que se llame como la
    // carpeta de trabajo. Devuelve el prefijo de la carpeta donde está el ejecutable («» o «Carpeta/»), o null con el motivo.
    public static string InspectZip(string zipPath, out string why)
    {
        why = null; string root = null; bool data = false;
        try
        {
            using (ZipArchive z = ZipFile.OpenRead(zipPath))
            {
                if (z.Entries.Count == 0 || z.Entries.Count > 20000) { why = "zip vacío o con demasiadas entradas"; return null; }
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    string n = e.FullName.Replace('\\', '/');
                    if (!SafeEntry(n)) { why = "ruta no válida en el zip: " + n; return null; }
                    string lower = n.ToLowerInvariant();
                    if (lower == ExeName.ToLowerInvariant() || lower.EndsWith("/" + ExeName.ToLowerInvariant()))
                    {
                        string pre = n.Substring(0, n.Length - ExeName.Length);
                        if (pre.Split('/').Length > 2) continue;   // solo en la raíz o en una carpeta de primer nivel
                        if (root == null || pre.Length < root.Length) root = pre;
                    }
                }
                if (root == null) { why = "el zip no contiene " + ExeName; return null; }
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    string n = e.FullName.Replace('\\', '/');
                    if (n.StartsWith(root + DataDir + "/", StringComparison.OrdinalIgnoreCase)) data = true;
                    if (n.Length > root.Length && n.StartsWith(root, StringComparison.Ordinal))
                    {
                        string top = n.Substring(root.Length).Split('/')[0];
                        if (top.Equals(WorkDirName, StringComparison.OrdinalIgnoreCase) || top.StartsWith("_update", StringComparison.OrdinalIgnoreCase)) { why = "el zip contiene " + top; return null; }
                    }
                }
                if (!data) { why = "el zip no contiene " + DataDir; return null; }
            }
        }
        catch (Exception e) { why = "zip dañado: " + e.Message; return null; }
        return root;
    }
    static bool SafeEntry(string n)
    {
        if (string.IsNullOrEmpty(n) || n.Length > 400) return false;
        if (n.StartsWith("/") || n.Contains(":") || n.Contains("\0")) return false;
        foreach (string part in n.Split('/')) if (part == ".." || part == ".") return false;
        return true;
    }

    // Extrae solo lo que cuelga de «root» dentro de «dest» (vacía o nueva). Comprueba de nuevo cada ruta final.
    public static void ExtractZip(string zipPath, string root, string dest)
    {
        if (Directory.Exists(dest)) Directory.Delete(dest, true);
        Directory.CreateDirectory(dest);
        string full = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using (ZipArchive z = ZipFile.OpenRead(zipPath))
            foreach (ZipArchiveEntry e in z.Entries)
            {
                string n = e.FullName.Replace('\\', '/');
                if (!SafeEntry(n)) throw new IOException("ruta no válida en el zip: " + n);
                if (!n.StartsWith(root, StringComparison.Ordinal) || n.Length == root.Length) continue;
                string rel = n.Substring(root.Length);
                string target = Path.GetFullPath(Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new IOException("ruta fuera de la carpeta: " + n);
                if (n.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                e.ExtractToFile(target, true);
            }
        if (!File.Exists(Path.Combine(dest, ExeName)) || !Directory.Exists(Path.Combine(dest, DataDir))) throw new IOException("faltan archivos tras extraer");
    }

    // ¿se puede escribir en la carpeta del juego? (Archivos de programa, carpetas de solo lectura…)
    public static bool CanWrite(string dir, out string why)
    {
        why = null;
        try
        {
            string w = Path.Combine(dir, WorkDirName); Directory.CreateDirectory(w);
            string f = Path.Combine(w, ".prueba_escritura"); File.WriteAllText(f, "ok"); File.Delete(f);
            // y renombrar algo de la carpeta del juego (lo que hará el ayudante): se prueba con un archivo propio
            string f2 = Path.Combine(dir, "_update_prueba.tmp"), f3 = Path.Combine(w, "_update_prueba2.tmp");
            File.WriteAllText(f2, "ok"); File.Move(f2, f3); File.Delete(f3);
            return true;
        }
        catch (Exception e) { why = e.Message; return false; }
    }

    static string Q(string s) { return "'" + (s ?? "").Replace("'", "''") + "'"; }

    // Script del ayudante. Lo escribe el juego (no viene del zip); solo mueve carpetas/archivos ya verificados y vuelve a abrir el
    // BackroomsEscape.exe que venía en el zip comprobado. Espera a que el juego se cierre, aparta lo viejo a «_update\viejo»,
    // coloca lo nuevo y, si algo falla, deshace todo y vuelve a abrir la versión anterior. Deja el resultado en _update_result.txt.
    public static string HelperScript(int pid, string gameDir, string newDir, string backupDir, string relaunchArgs, string version, bool simulateFailure)
    {
        StringBuilder s = new StringBuilder();
        s.AppendLine("# Backrooms: Escapa — ayudante de actualización (escrito por el juego; se borra al terminar)");
        s.AppendLine("$ErrorActionPreference = 'Stop'");
        s.AppendLine("$gamePid = " + pid);
        s.AppendLine("$gameDir = " + Q(gameDir));
        s.AppendLine("$newDir = " + Q(newDir));
        s.AppendLine("$backup = " + Q(backupDir));
        s.AppendLine("$work = Split-Path -Parent $newDir");
        s.AppendLine("$exe = Join-Path $gameDir " + Q(ExeName));
        s.AppendLine("$relaunchArgs = " + Q(relaunchArgs));
        s.AppendLine("$version = " + Q(version));
        s.AppendLine("$simulate = $" + (simulateFailure ? "true" : "false"));
        s.AppendLine("$log = Join-Path $gameDir " + Q(LogFile));
        s.AppendLine("$result = Join-Path $gameDir " + Q(ResultFile));
        s.AppendLine(@"function L($m) { try { Add-Content -LiteralPath $log -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $m) -Encoding UTF8 } catch {} }
function Retry([scriptblock]$b) { for ($i = 0; $i -lt 40; $i++) { try { & $b; return } catch { if ($i -ge 39) { throw }; Start-Sleep -Milliseconds 250 } } }
function Relaunch() {
  try { if ($relaunchArgs -match '-batchmode') { Start-Process -FilePath $exe -ArgumentList $relaunchArgs -WorkingDirectory $gameDir -WindowStyle Hidden } elseif ($relaunchArgs) { Start-Process -FilePath $exe -ArgumentList $relaunchArgs -WorkingDirectory $gameDir } else { Start-Process -FilePath $exe -WorkingDirectory $gameDir } } catch { L ('no se pudo abrir el juego: ' + $_) }
}
L ('ayudante: actualizar a v' + $version + ' en ' + $gameDir)
try { Wait-Process -Id $gamePid -Timeout 120 -ErrorAction SilentlyContinue } catch {}
if (Get-Process -Id $gamePid -ErrorAction SilentlyContinue) { L 'el juego no se cerró a tiempo: no se toca nada'; Set-Content -LiteralPath $result -Value ('fail|el juego no se cerró a tiempo') -Encoding UTF8; exit 1 }
Start-Sleep -Milliseconds 400
$keep = $false; $moved = New-Object System.Collections.ArrayList; $placed = New-Object System.Collections.ArrayList
try {
  if (-not (Test-Path -LiteralPath (Join-Path $newDir 'BackroomsEscape.exe'))) { throw 'falta el ejecutable nuevo' }
  New-Item -ItemType Directory -Force -Path $backup | Out-Null
  foreach ($it in @(Get-ChildItem -LiteralPath $newDir -Force)) {
    $dst = Join-Path $gameDir $it.Name
    if (Test-Path -LiteralPath $dst) { Retry { Move-Item -LiteralPath $dst -Destination (Join-Path $backup $it.Name) -Force }; [void]$moved.Add($it.Name) }
    if ($simulate -and $placed.Count -ge 1) { throw 'fallo simulado (prueba de vuelta atrás)' }
    Retry { Move-Item -LiteralPath $it.FullName -Destination $dst -Force }; [void]$placed.Add($it.Name)
  }
  if (-not (Test-Path -LiteralPath $exe)) { throw 'falta el ejecutable tras copiar' }
  L ('archivos sustituidos: ' + ($placed -join ', '))
  Set-Content -LiteralPath $result -Value ('ok|' + $version) -Encoding UTF8
} catch {
  $err = '' + $_
  L ('ERROR: ' + $err + ' -> vuelta atrás')
  foreach ($n in $placed) { try { Retry { Remove-Item -LiteralPath (Join-Path $gameDir $n) -Recurse -Force } } catch { L ('no se pudo quitar ' + $n) } }
  foreach ($n in $moved) { try { Retry { Move-Item -LiteralPath (Join-Path $backup $n) -Destination (Join-Path $gameDir $n) -Force } } catch { $keep = $true; L ('no se pudo restaurar ' + $n + ' (queda en ' + $backup + '): ' + $_) } }
  Set-Content -LiteralPath $result -Value ('fail|' + $err) -Encoding UTF8
}
if (-not $keep) { try { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction Stop } catch { L ('limpieza parcial: ' + $_) } }
L 'abriendo el juego'
Relaunch");
        return s.ToString();
    }

    // argumentos con los que se volverá a abrir el juego: los mismos que ahora (normalmente ninguno); -logFile pasa a otro archivo
    public static string RelaunchArgs(string[] args)
    {
        List<string> o = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "-logFile" && i + 1 < args.Length) { o.Add(a); o.Add(args[++i] + ".relanzado.log"); continue; }
            o.Add(a);
        }
        StringBuilder sb = new StringBuilder();
        foreach (string a in o)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) sb.Append(a);
            else sb.Append('"').Append(a.Replace("\"", "\\\"")).Append('"');
        }
        return sb.ToString();
    }

    public static string Mb(long b) { return (b / 1048576.0).ToString("0") + " MB"; }
}
