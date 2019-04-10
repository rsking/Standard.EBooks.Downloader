// -----------------------------------------------------------------------
// <copyright file="ExtensionMethods.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Common
{
    using System.IO;
    using System.Net.Http;
    using System.Threading.Tasks;

    /// <summary>
    /// Extension methods.
    /// </summary>
    public static class ExtensionMethods
    {
        /// <summary>
        /// Checks to see whether the specified specified <see cref="System.Uri" /> needs to be downloaded based on the last modified date.
        /// </summary>
        /// <param name="uri">The uri to check.</param>
        /// <param name="dateTime">The last modified date time.</param>
        /// <param name="clientFactory">The client factory.</param>
        /// <returns>Returns <see langword="true"/> if the last modified date does not match; otherwise <see langword="false"/>.</returns>
        public static async Task<bool> ShouldDownloadAsync(this System.Uri uri, System.DateTime dateTime, IHttpClientFactory clientFactory = null)
        {
            System.DateTimeOffset? lastModified = null;
            var client = clientFactory == null
                ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None })
                : clientFactory.CreateClient("header");

            using (var request = new HttpRequestMessage(HttpMethod.Head, uri))
            {
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                lastModified = response.Content.Headers.LastModified;
            }

            if (clientFactory == null)
            {
                client?.Dispose();
            }

            if (lastModified.HasValue)
            {
                // check down to a 2 second difference
                var difference = dateTime.ToLocalTime() - lastModified.Value.DateTime;
                return System.Math.Abs(difference.TotalSeconds) > 2D;
            }

            return true;
        }

        /// <summary>
        /// Downloads the specified <see cref="System.Uri" /> as a file.
        /// </summary>
        /// <param name="uri">The uri to download.</param>
        /// <param name="fileName">The file name to download to.</param>
        /// <param name="overwrite">Set to true to overwrite.</param>
        /// <param name="clientFactory">The client factory.</param>
        /// <returns>The task to download the file.</returns>
        public static async Task DownloadAsFileAsync(this System.Uri uri, string fileName, bool overwrite, IHttpClientFactory clientFactory = null)
        {
            var client = clientFactory == null ? new HttpClient() : clientFactory.CreateClient();

            var response = await client.GetAsync(uri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await response.Content.ReadAsFileAsync(fileName, overwrite).ConfigureAwait(false);

            if (clientFactory == null)
            {
                client?.Dispose();
            }
        }

        /// <summary>
        /// Downloads the specified <see cref="System.Uri" /> as a string.
        /// </summary>
        /// <param name="uri">The uri to download.</param>
        /// <returns>The task to download the string.</returns>
        /// <param name="clientFactory">The client factory.</param>
        public static async Task<string> DownloadAsStringAsync(this System.Uri uri, IHttpClientFactory clientFactory = null)
        {
            var client = clientFactory == null ? new HttpClient() : clientFactory.CreateClient();

            var response = await client.GetAsync(uri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var stringValue = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (clientFactory == null)
            {
                client?.Dispose();
            }

            return stringValue;
        }

        private static Task ReadAsFileAsync(this HttpContent content, string fileName, bool overwrite)
        {
            var pathName = Path.GetFullPath(fileName);
            if (!overwrite && File.Exists(fileName))
            {
                throw new System.InvalidOperationException($"File {pathName} already exists.");
            }

            FileStream fileStream = null;
            try
            {
                fileStream = new FileStream(pathName, FileMode.Create, FileAccess.Write, FileShare.None);

                return content.CopyToAsync(fileStream).ContinueWith(_ =>
                {
                    fileStream.Close();
                    var dateTimeOffset = content.Headers.LastModified;
                    if (dateTimeOffset.HasValue)
                    {
                        var fileSystemInfo = new FileInfo(pathName) { LastWriteTime = dateTimeOffset.Value.DateTime };
                    }
                });
            }
            catch
            {
                fileStream?.Close();
                throw;
            }
        }
    }
}