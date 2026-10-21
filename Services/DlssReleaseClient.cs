using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace dlss_updater.Services
{
    [DataContract]
    internal sealed class DlssReleaseAsset
    {
        [DataMember(Name = "version", IsRequired = true)]
        public string Version { get; set; }

        [DataMember(Name = "url", IsRequired = true)]
        public string Url { get; set; }

        [DataMember(Name = "sha256", EmitDefaultValue = false)]
        public string Sha256 { get; set; }
    }

    [DataContract]
    internal sealed class GitHubRelease
    {
        [DataMember(Name = "assets")]
        public List<GitHubReleaseAsset> Assets { get; set; }
    }

    [DataContract]
    internal sealed class GitHubReleaseAsset
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "digest")]
        public string Digest { get; set; }
    }

    internal static class DlssReleaseClient
    {
        private const string ManifestUrl =
            "https://github.com/sparepillowgit/dlss-updater-files/releases/latest/download/dlss_manifest.json";
        private const string LatestReleaseApiUrl =
            "https://api.github.com/repos/sparepillowgit/dlss-updater-files/releases/latest";

        private static readonly HttpClient HttpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("DLSSUpdater", "1.0"));

            return client;
        }

        public static async Task<Dictionary<string, DlssReleaseAsset>>
            GetLatestManifestAsync()
        {
            string json =
                await HttpClient.GetStringAsync(ManifestUrl);

            DataContractJsonSerializerSettings serializerSettings =
                new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                };

            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(
                    typeof(Dictionary<string, DlssReleaseAsset>),
                    serializerSettings);

            Dictionary<string, DlssReleaseAsset> downloadedManifest;

            using (MemoryStream stream =
                new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                downloadedManifest =
                    (Dictionary<string, DlssReleaseAsset>)
                    serializer.ReadObject(stream);
            }

            Dictionary<string, DlssReleaseAsset> manifest =
                new Dictionary<string, DlssReleaseAsset>(
                    StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> releaseDigests = null;

            foreach (KeyValuePair<string, DlssReleaseAsset> item
                in downloadedManifest)
            {
                if (!IsSupportedDll(item.Key) ||
                    item.Value == null ||
                    string.IsNullOrWhiteSpace(item.Value.Version) ||
                    !IsTrustedDownloadUrl(item.Value.Url))
                {
                    continue;
                }

                if (!IsSha256(item.Value.Sha256))
                {
                    if (releaseDigests == null)
                    {
                        releaseDigests = await GetLatestReleaseAssetSha256Async();
                    }

                    string releaseDigest;
                    if (releaseDigests.TryGetValue(item.Key, out releaseDigest))
                    {
                        item.Value.Sha256 = releaseDigest;
                    }
                }

                if (!IsSha256(item.Value.Sha256))
                {
                    throw new InvalidDataException(
                        "GitHub did not provide a SHA-256 digest for " + item.Key + ".");
                }

                item.Value.Sha256 = item.Value.Sha256.ToLowerInvariant();

                manifest[item.Key] = item.Value;
            }

            if (manifest.Count == 0)
            {
                throw new InvalidDataException(
                    "The release manifest did not contain any supported DLSS files.");
            }

            return manifest;
        }

        public static async Task<string> DownloadToTemporaryFileAsync(
            string url,
            string fileName)
        {
            string temporaryPath = Path.Combine(
                Path.GetTempPath(),
                "dlss-updater-" + Guid.NewGuid().ToString("N") + "-" + fileName);

            try
            {
                using (HttpResponseMessage response =
                    await HttpClient.GetAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (Stream source = await response.Content.ReadAsStreamAsync())
                    using (FileStream destination = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        await source.CopyToAsync(destination);
                    }
                }

                return temporaryPath;
            }
            catch
            {
                TryDeleteFile(temporaryPath);
                throw;
            }
        }

        public static bool TryGetCachedFile(
            string fileName,
            string version,
            string expectedSha256,
            out string cachedPath)
        {
            cachedPath = GetCachedFilePath(fileName, version);

            try
            {
                if (!File.Exists(cachedPath) || new FileInfo(cachedPath).Length == 0)
                {
                    return false;
                }

                if (FileMatchesSha256(cachedPath, expectedSha256))
                {
                    return true;
                }

                TryDeleteFile(cachedPath);
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> DownloadToCacheAsync(
            string url,
            string fileName,
            string version,
            string expectedSha256)
        {
            string cacheDirectory = GetCacheDirectory();
            string cachePath = GetCachedFilePath(fileName, version);

            Directory.CreateDirectory(cacheDirectory);

            string downloadedPath = null;
            string stagingPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                downloadedPath = await DownloadToTemporaryFileAsync(url, fileName);
                if (!FileMatchesSha256(downloadedPath, expectedSha256))
                {
                    throw new InvalidDataException(
                        "The SHA-256 check failed for " + fileName + ". The download was discarded.");
                }

                File.Copy(downloadedPath, stagingPath, false);

                if (File.Exists(cachePath))
                {
                    File.Replace(stagingPath, cachePath, null);
                }
                else
                {
                    File.Move(stagingPath, cachePath);
                }

                RemoveOlderCachedVersions(cacheDirectory, fileName, cachePath);
                return cachePath;
            }
            finally
            {
                TryDeleteFile(downloadedPath);
                TryDeleteFile(stagingPath);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static async Task<Dictionary<string, string>> GetLatestReleaseAssetSha256Async()
        {
            using (HttpResponseMessage response = await HttpClient.GetAsync(LatestReleaseApiUrl))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
                GitHubRelease release;
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    release = (GitHubRelease)serializer.ReadObject(stream);
                }

                Dictionary<string, string> digests =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (release?.Assets != null)
                {
                    foreach (GitHubReleaseAsset asset in release.Assets)
                    {
                        if (asset != null &&
                            !string.IsNullOrWhiteSpace(asset.Name) &&
                            asset.Digest != null &&
                            asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        {
                            string digest = asset.Digest.Substring("sha256:".Length);
                            if (IsSha256(digest))
                            {
                                digests[asset.Name] = digest;
                            }
                        }
                    }
                }

                return digests;
            }
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }

            foreach (char character in value)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FileMatchesSha256(string path, string expectedSha256)
        {
            if (!IsSha256(expectedSha256))
            {
                return false;
            }

            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(stream);
                StringBuilder result = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    result.Append(value.ToString("x2"));
                }

                return string.Equals(result.ToString(), expectedSha256, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string GetCacheDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DLSS Updater",
                "Cache");
        }

        private static string GetCachedFilePath(
            string fileName,
            string version)
        {
            return Path.Combine(
                GetCacheDirectory(),
                fileName + "." + version + ".cache");
        }

        private static void RemoveOlderCachedVersions(
            string cacheDirectory,
            string fileName,
            string currentCachePath)
        {
            try
            {
                foreach (string path in Directory.GetFiles(
                    cacheDirectory,
                    fileName + ".*.cache"))
                {
                    if (!string.Equals(
                        path,
                        currentCachePath,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(path);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsSupportedDll(string fileName)
        {
            return string.Equals(fileName, "nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "nvngx_dlssd.dll", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "nvngx_dlssg.dll", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTrustedDownloadUrl(string url)
        {
            Uri uri;

            return Uri.TryCreate(url, UriKind.Absolute, out uri) &&
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith(
                    "/sparepillowgit/dlss-updater-files/releases/",
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
