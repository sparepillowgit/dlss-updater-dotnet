using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace dlss_updater.Services
{
    internal class ScannedFile
    {
        public string Path { get; set; }

        public string Version { get; set; }
    }

    internal static class FolderScanner
    {
        public static List<ScannedFile> FindFiles(
            string folderPath,
            string fileName,
            ICollection<string> warningPaths = null)
        {
            List<ScannedFile> results =
                new List<ScannedFile>();

            if (string.IsNullOrEmpty(folderPath) ||
                !Directory.Exists(folderPath))
            {
                return results;
            }

            SearchDirectory(
                folderPath,
                fileName,
                results,
                warningPaths);

            return results;
        }

        private static void SearchDirectory(
            string folderPath,
            string fileName,
            List<ScannedFile> results,
            ICollection<string> warningPaths)
        {
            try
            {
                string[] files =
                    Directory.GetFiles(
                        folderPath,
                        fileName);

                foreach (string file in files)
                {
                    results.Add(
                        new ScannedFile
                        {
                            Path = file,
                            Version = GetFileVersion(file)
                        });
                }
            }
            catch
            {
                warningPaths?.Add(folderPath);
            }

            string[] directories;

            try
            {
                directories =
                    Directory.GetDirectories(
                        folderPath);
            }
            catch
            {
                warningPaths?.Add(folderPath);
                return;
            }

            foreach (string directory in directories)
            {
                SearchDirectory(
                    directory,
                    fileName,
                    results,
                    warningPaths);
            }
        }

        private static string GetFileVersion(
            string filePath)
        {
            try
            {
                FileVersionInfo versionInfo =
                    FileVersionInfo.GetVersionInfo(
                        filePath);

                if (!string.IsNullOrEmpty(
                    versionInfo.FileVersion))
                {
                    return versionInfo.FileVersion.Replace(",", ".");
                }
            }
            catch
            {
            }

            return "Unknown";
        }
    }
}
