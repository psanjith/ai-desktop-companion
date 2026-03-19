using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

public class UpdateManager : MonoBehaviour
{
    [Serializable]
    private class UpdateManifest
    {
        public string latestVersion;
        public string url;
        public string sha256;
        public bool mandatory;
        public string notes;
    }

    [Serializable]
    private class UpdaterConfig
    {
        public string manifestUrl = "https://github.com/psanjith/ai-desktop-companion/releases/latest/download/update.json";
        public bool autoDownload = true;
        public float startupDelaySeconds = 5f;
    }

    private static bool _bootstrapped;
    private const string UpdaterConfigRelativePath = "updater/config.json";

    private UpdaterConfig _config;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_bootstrapped) return;
        _bootstrapped = true;

        var go = new GameObject("AutoUpdateManager");
        DontDestroyOnLoad(go);
        go.AddComponent<UpdateManager>();
    }

    private void Start()
    {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        _config = LoadConfig();
        StartCoroutine(CheckForUpdatesRoutine());
#endif
    }

    private UpdaterConfig LoadConfig()
    {
        var cfg = new UpdaterConfig();
        try
        {
            string cfgPath = Path.Combine(Application.streamingAssetsPath, UpdaterConfigRelativePath);
            if (File.Exists(cfgPath))
            {
                string json = File.ReadAllText(cfgPath);
                var parsed = JsonUtility.FromJson<UpdaterConfig>(json);
                if (parsed != null)
                    cfg = parsed;
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Updater] Failed to load config: {ex.Message}");
        }

        return cfg;
    }

    private IEnumerator CheckForUpdatesRoutine()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, _config.startupDelaySeconds));

        if (string.IsNullOrWhiteSpace(_config.manifestUrl))
            yield break;

        UnityEngine.Debug.Log($"[Updater] Checking manifest: {_config.manifestUrl}");

        using (var req = UnityWebRequest.Get(_config.manifestUrl))
        {
            req.timeout = 20;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                UnityEngine.Debug.LogWarning($"[Updater] Manifest fetch failed: {req.error}");
                yield break;
            }

            UpdateManifest manifest = null;
            try
            {
                manifest = JsonUtility.FromJson<UpdateManifest>(req.downloadHandler.text);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Updater] Invalid manifest JSON: {ex.Message}");
                yield break;
            }

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.latestVersion) || string.IsNullOrWhiteSpace(manifest.url))
            {
                UnityEngine.Debug.LogWarning("[Updater] Manifest missing required fields.");
                yield break;
            }

            if (!IsRemoteVersionNewer(Application.version, manifest.latestVersion))
            {
                UnityEngine.Debug.Log("[Updater] Already on latest version.");
                yield break;
            }

            UnityEngine.Debug.Log($"[Updater] Update found: {Application.version} -> {manifest.latestVersion}");

            if (!_config.autoDownload)
            {
                UnityEngine.Debug.Log("[Updater] autoDownload=false; skipping download.");
                yield break;
            }

            yield return DownloadAndInstall(manifest);
        }
    }

    private IEnumerator DownloadAndInstall(UpdateManifest manifest)
    {
        string updatesDir = Path.Combine(Application.persistentDataPath, "updates");
        Directory.CreateDirectory(updatesDir);

        string packageExt = manifest.url != null && manifest.url.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            ? ".tar.gz"
            : ".zip";
        string packagePath = Path.Combine(updatesDir, $"DesktopCompanion-{manifest.latestVersion}{packageExt}");
        UnityEngine.Debug.Log($"[Updater] Downloading update to: {packagePath}");

        using (var req = UnityWebRequest.Get(manifest.url))
        {
            req.timeout = 120;
            req.downloadHandler = new DownloadHandlerFile(packagePath);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                UnityEngine.Debug.LogWarning($"[Updater] Download failed: {req.error}");
                yield break;
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.sha256))
        {
            string actual = ComputeSha256(packagePath);
            if (!string.Equals(actual, manifest.sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                UnityEngine.Debug.LogWarning($"[Updater] SHA256 mismatch. expected={manifest.sha256} actual={actual}");
                yield break;
            }
        }

        string installerScript = Path.Combine(Application.streamingAssetsPath, "updater", "install_update.sh");
        if (!File.Exists(installerScript))
        {
            UnityEngine.Debug.LogWarning($"[Updater] Installer script not found: {installerScript}");
            yield break;
        }

        string appPath = GetCurrentAppBundlePath();
        if (string.IsNullOrWhiteSpace(appPath))
        {
            UnityEngine.Debug.LogWarning("[Updater] Could not resolve current .app path.");
            yield break;
        }

        TryChmodExecutable(installerScript);

        string args = $"\"{installerScript}\" \"{appPath}\" \"{packagePath}\" \"{Path.GetFileNameWithoutExtension(appPath)}\"";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            Process.Start(psi);

            UnityEngine.Debug.Log("[Updater] Installer started. Quitting for update...");
            Application.Quit();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Updater] Failed to launch installer: {ex.Message}");
        }
    }

    private static bool IsRemoteVersionNewer(string current, string remote)
    {
        try
        {
            var c = ParseVersion(current);
            var r = ParseVersion(remote);
            return r > c;
        }
        catch
        {
            return !string.Equals(current, remote, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Version ParseVersion(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return new Version(0, 0, 0);
        v = v.Trim();
        if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            v = v.Substring(1);

        string[] parts = v.Split('.');
        while (parts.Length < 3)
            v += ".0";

        return new Version(v);
    }

    private static string ComputeSha256(string filePath)
    {
        using (var stream = File.OpenRead(filePath))
        using (var sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    private static string GetCurrentAppBundlePath()
    {
        try
        {
            // On macOS player, dataPath is typically: /Applications/DesktopCompanion.app/Contents
            var contentsDir = new DirectoryInfo(Application.dataPath);
            var appDir = contentsDir.Parent;
            return appDir?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static void TryChmodExecutable(string filePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = $"+x \"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Updater] chmod failed: {ex.Message}");
        }
    }
}
