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

        public List<string> Errors { get; } = new List<string>();

        public string GetSummary()
        {
            string summary =
                "Updated " + UpdatedFiles + " file(s). " +
                AlreadyCurrentFiles + " file(s) were already current.";

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
            bool overwriteExistingBackups)
        {
            DlssUpdateResult result = new DlssUpdateResult();

            if (installedFiles == null || installedFiles.Count == 0)
            {
                result.Errors.Add(
                    "No supported DLSS files were found in the selected folder.");
                return result;
            }

            Dictionary<string, DlssReleaseAsset> manifest =
                await DlssReleaseClient.GetLatestManifestAsync();

            Dictionary<string, string> downloadedFiles =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (ScannedFile installedFile in installedFiles)
                {
                    string fileName = Path.GetFileName(installedFile.Path);
                    DlssReleaseAsset releaseAsset;

                    if (!manifest.TryGetValue(fileName, out releaseAsset))
                    {
                        result.Errors.Add(
                            "No release entry was found for " + fileName + ".");
                        continue;
                    }

                    Version installedVersion;
                    Version latestVersion;

                    if (!Version.TryParse(installedFile.Version, out installedVersion) ||
                        !Version.TryParse(releaseAsset.Version, out latestVersion))
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

                    string downloadedPath;

                    if (!downloadedFiles.TryGetValue(fileName, out downloadedPath))
                    {
                        try
                        {
                            downloadedPath =
                                await DlssReleaseClient.DownloadToTemporaryFileAsync(
                                    releaseAsset.Url,
                                    fileName);

                            downloadedFiles[fileName] = downloadedPath;
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add(
                                "Could not download " + fileName + ": " + ex.Message);
                            continue;
                        }
                    }

                    try
                    {
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
            }
            finally
            {
                foreach (string downloadedPath in downloadedFiles.Values)
                {
                    if (File.Exists(downloadedPath))
                    {
                        File.Delete(downloadedPath);
                    }
                }
            }

            return result;
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
