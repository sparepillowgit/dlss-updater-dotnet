using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace dlss_updater.Services
{
    internal sealed class DlssUpdateResult
    {
        public int UpdatedFiles { get; set; }

        public int AlreadyCurrentFiles { get; set; }

        public List<string> SkippedFiles { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();

        public string GetSummary()
        {
            string summary =
                "Updated " + UpdatedFiles + " file(s). " +
                AlreadyCurrentFiles + " file(s) were already current.";

            if (SkippedFiles.Count > 0)
            {
                summary += " " + SkippedFiles.Count +
                    " file(s) skipped for version compatibility.";
            }

            if (Errors.Count > 0)
            {
                summary +=
                    Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, Errors);
            }

            return summary;
        }
    }

    internal static class GameFolderUpdateService
    {
        public static async Task<DlssUpdateResult> UpdateAsync(
            IList<ScannedFile> installedFiles,
            bool overwriteExistingBackups,
            IProgress<string> progress = null)
        {
            DlssUpdateResult result = new DlssUpdateResult();

            if (installedFiles == null || installedFiles.Count == 0)
            {
                result.Errors.Add(
                    "No supported DLSS files were found in the selected folder.");
                return result;
            }

            Dictionary<string, DlssReleaseAsset> manifest =
                await GetLatestManifestAsync(progress);

            Dictionary<string, string> downloadedFiles =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int fileIndex = 0;

            foreach (ScannedFile installedFile in installedFiles)
            {
                fileIndex++;
                string fileName = Path.GetFileName(installedFile.Path);
                progress?.Report(
                    "Checking " + fileName + " (" + fileIndex +
                    " of " + installedFiles.Count + ")…");

                DlssReleaseAsset releaseAsset;

                if (!manifest.TryGetValue(fileName, out releaseAsset))
                {
                    result.Errors.Add(
                        "No release entry was found for " + fileName + ".");
                    continue;
                }

                Version installedVersion;
                Version latestVersion;

                if (!Version.TryParse(installedFile.Version, out installedVersion))
                {
                    result.Errors.Add(
                        "Could not compare the version for " + installedFile.Path + ".");
                    continue;
                }

                if (IsLegacyDlss(fileName, installedVersion))
                {
                    releaseAsset = DlssReleaseClient.GetLegacyDlssAsset();
                }

                if (!Version.TryParse(releaseAsset.Version, out latestVersion))
                {
                    result.Errors.Add(
                        "Could not compare the version for " + installedFile.Path + ".");
                    continue;
                }

                if (latestVersion <= installedVersion)
                {
                    result.AlreadyCurrentFiles++;
                    continue;
                }

                if (!AreVersionsCompatible(fileName, installedVersion, latestVersion))
                {
                    result.SkippedFiles.Add(
                        "Skipped " + fileName + ": updating from " +
                        installedVersion + " to " + latestVersion +
                        " crosses an unsupported major version boundary.");
                    continue;
                }

                string downloadedPath;
                string downloadKey = fileName + "|" + latestVersion;

                if (!downloadedFiles.TryGetValue(downloadKey, out downloadedPath))
                {
                    if (DlssReleaseClient.TryGetCachedFile(
                        fileName,
                        latestVersion.ToString(),
                        releaseAsset.Sha256,
                        out downloadedPath))
                    {
                        progress?.Report("Using cached " + fileName + "…");
                    }
                    else
                    {
                        try
                        {
                            progress?.Report("Downloading " + fileName + "…");
                            downloadedPath =
                                await DlssReleaseClient.DownloadToCacheAsync(
                                    releaseAsset.Url,
                                    fileName,
                                    latestVersion.ToString(),
                                    releaseAsset.Sha256);
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add(
                                "Could not download " + fileName + ": " + ex.Message);
                            continue;
                        }
                    }

                    downloadedFiles[downloadKey] = downloadedPath;
                }

                try
                {
                    progress?.Report("Updating " + fileName + "…");
                    BackupArchiveService.CreateBackup(
                        installedFile.Path,
                        installedVersion.ToString(),
                        overwriteExistingBackups);

                    ReplaceFile(downloadedPath, installedFile.Path);
                    installedFile.Version = latestVersion.ToString();
                    result.UpdatedFiles++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add(
                        "Could not update " + installedFile.Path + ": " + ex.Message);
                }
            }

            return result;
        }

        private static bool AreVersionsCompatible(
            string fileName,
            Version installedVersion,
            Version latestVersion)
        {
            int installedMajor = GetDlssCompatibilityMajor(installedVersion.Major);
            int latestMajor = GetDlssCompatibilityMajor(latestVersion.Major);

            if (string.Equals(
                fileName,
                "nvngx_dlssg.dll",
                StringComparison.OrdinalIgnoreCase))
            {
                installedMajor = NormalizeFrameGenerationMajor(installedMajor);
                latestMajor = NormalizeFrameGenerationMajor(latestMajor);
            }

            if (installedMajor == latestMajor)
            {
                return true;
            }

            return (installedMajor == 2 || installedMajor == 3) &&
                (latestMajor == 2 || latestMajor == 3);
        }

        private static bool IsLegacyDlss(string fileName, Version installedVersion)
        {
            return string.Equals(
                    fileName,
                    "nvngx_dlss.dll",
                    StringComparison.OrdinalIgnoreCase) &&
                GetDlssCompatibilityMajor(installedVersion.Major) == 1;
        }

        private static int NormalizeFrameGenerationMajor(int major)
        {
            return major == 1 ? 3 : major;
        }

        private static int GetDlssCompatibilityMajor(int fileVersionMajor)
        {
            return fileVersionMajor >= 100
                ? fileVersionMajor / 100
                : fileVersionMajor;
        }

        private static async Task<Dictionary<string, DlssReleaseAsset>>
            GetLatestManifestAsync(IProgress<string> progress)
        {
            progress?.Report("Checking for the latest DLSS versions…");
            return await DlssReleaseClient.GetLatestManifestAsync();
        }

        private static void ReplaceFile(
            string downloadedPath,
            string destinationPath)
        {
            string stagingPath = Path.Combine(
                Path.GetDirectoryName(destinationPath),
                "." + Path.GetFileName(destinationPath) +
                "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                File.Copy(
                    downloadedPath,
                    stagingPath,
                    false);

                File.Replace(
                    stagingPath,
                    destinationPath,
                    null);
            }
            catch
            {
                if (File.Exists(stagingPath))
                {
                    File.Delete(stagingPath);
                }

                throw;
            }
        }
    }
}
