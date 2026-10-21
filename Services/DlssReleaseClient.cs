using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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
    }

    internal static class DlssReleaseClient
    {
        private const string ManifestUrl =
            "https://github.com/sparepillowgit/dlss-updater-files/releases/latest/download/dlss_manifest.json";

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
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                throw;
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
