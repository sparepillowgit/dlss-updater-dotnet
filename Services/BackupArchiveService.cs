using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace dlss_updater.Services
{
    internal static class BackupArchiveService
    {
        private static readonly Regex BackupFileNamePattern =
            new Regex(
                @"^(?<dll>nvngx(?:_dlss(?:d|g)?)?\.dll)\.(?<version>.+)\.dubackup\.zip$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string CreateBackup(
            string dllPath,
            string version,
            bool overwriteExisting)
        {
            string backupPath = Path.Combine(
                Path.GetDirectoryName(dllPath),
                Path.GetFileName(dllPath) +
                "." +
                version +
                ".dubackup.zip");

            if (File.Exists(backupPath) && !overwriteExisting)
            {
                return backupPath;
            }

            string temporaryBackupPath =
                backupPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                using (FileStream archiveStream = new FileStream(
                    temporaryBackupPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None))
                using (ZipArchive archive = new ZipArchive(
                    archiveStream,
                    ZipArchiveMode.Create,
                    false))
                {
                    ZipArchiveEntry entry = archive.CreateEntry(
                        Path.GetFileName(dllPath),
                        CompressionLevel.Optimal);

                    using (Stream source = File.OpenRead(dllPath))
                    using (Stream destination = entry.Open())
                    {
                        source.CopyTo(destination);
                    }
                }

                if (File.Exists(backupPath))
                {
                    File.Replace(
                        temporaryBackupPath,
                        backupPath,
                        null);
                }
                else
                {
                    File.Move(
                        temporaryBackupPath,
                        backupPath);
                }

                return backupPath;
            }
            catch
            {
                if (File.Exists(temporaryBackupPath))
                {
                    File.Delete(temporaryBackupPath);
                }

                throw;
            }
        }

        public static BackupOperationResult RestoreLatestBackups(
            IList<ScannedFile> backups)
        {
            BackupOperationResult result = new BackupOperationResult();
            Dictionary<string, ScannedFile> latestBackups =
                new Dictionary<string, ScannedFile>(StringComparer.OrdinalIgnoreCase);

            if (backups == null)
            {
                result.Errors.Add("No backups were selected.");
                return result;
            }

            foreach (ScannedFile backup in backups)
            {
                Match match = BackupFileNamePattern.Match(
                    Path.GetFileName(backup.Path));

                if (!match.Success)
                {
                    result.Errors.Add("Unrecognized backup file: " + backup.Path);
                    continue;
                }

                Version candidateVersion;
                if (!Version.TryParse(match.Groups["version"].Value, out candidateVersion))
                {
                    result.Errors.Add("Could not read the backup version: " + backup.Path);
                    continue;
                }

                string destinationPath = Path.Combine(
                    Path.GetDirectoryName(backup.Path),
                    match.Groups["dll"].Value);

                ScannedFile current;
                if (!latestBackups.TryGetValue(destinationPath, out current))
                {
                    latestBackups[destinationPath] = backup;
                    continue;
                }

                Match currentMatch = BackupFileNamePattern.Match(
                    Path.GetFileName(current.Path));
                Version currentVersion;
                if (Version.TryParse(currentMatch.Groups["version"].Value, out currentVersion) &&
                    candidateVersion > currentVersion)
                {
                    latestBackups[destinationPath] = backup;
                }
            }

            foreach (KeyValuePair<string, ScannedFile> item in latestBackups)
            {
                try
                {
                    RestoreBackup(item.Value.Path, item.Key);
                    result.RestoredFiles++;

                    try
                    {
                        File.Delete(item.Value.Path);
                        result.DeletedFiles++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(
                            "Restored " + item.Key + " but could not delete its backup " +
                            item.Value.Path + ": " + ex.Message);
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add(
                        "Could not restore " + item.Key + ": " + ex.Message);
                }
            }

            return result;
        }

        public static BackupOperationResult DeleteBackups(
            IList<ScannedFile> backups)
        {
            BackupOperationResult result = new BackupOperationResult();

            if (backups == null)
            {
                result.Errors.Add("No backups were selected.");
                return result;
            }

            foreach (ScannedFile backup in backups)
            {
                try
                {
                    File.Delete(backup.Path);
                    result.DeletedFiles++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add(
                        "Could not delete " + backup.Path + ": " + ex.Message);
                }
            }

            return result;
        }

        private static void RestoreBackup(
            string backupPath,
            string destinationPath)
        {
            string expectedFileName = Path.GetFileName(destinationPath);
            string stagingPath = Path.Combine(
                Path.GetDirectoryName(destinationPath),
                "." + expectedFileName + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (FileStream archiveStream = File.OpenRead(backupPath))
                using (ZipArchive archive = new ZipArchive(
                    archiveStream,
                    ZipArchiveMode.Read,
                    false))
                {
                    ZipArchiveEntry entry = null;
                    foreach (ZipArchiveEntry archiveEntry in archive.Entries)
                    {
                        if (string.Equals(
                            archiveEntry.FullName,
                            expectedFileName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            if (entry != null)
                            {
                                throw new InvalidDataException(
                                    "The backup contains duplicate DLL entries.");
                            }
                            entry = archiveEntry;
                        }
                    }

                    if (entry == null)
                    {
                        throw new InvalidDataException(
                            "The backup does not contain " + expectedFileName + ".");
                    }

                    using (Stream source = entry.Open())
                    using (FileStream destination = new FileStream(
                        stagingPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        source.CopyTo(destination);
                    }
                }

                if (File.Exists(destinationPath))
                {
                    File.Replace(stagingPath, destinationPath, null);
                }
                else
                {
                    File.Move(stagingPath, destinationPath);
                }
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

    internal sealed class BackupOperationResult
    {
        public int RestoredFiles { get; set; }

        public int DeletedFiles { get; set; }

        public List<string> Errors { get; } = new List<string>();

        public string GetSummary(bool restoring)
        {
            string summary = restoring
                ? "Restored " + RestoredFiles + " file(s)."
                : "Deleted " + DeletedFiles + " backup file(s).";

            if (Errors.Count > 0)
            {
                summary += Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, Errors);
            }

            return summary;
        }
    }
}
