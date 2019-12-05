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
        public static async Task<bool> ShouldDownloadAsync(this System.Uri uri, System.DateTime dateTime, IHttpClientFactory? clientFactory = null)
        {
            var client = clientFactory is null
                ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None })
                : clientFactory.CreateClient("header");

            System.DateTimeOffset? lastModified = null;
            using (var request = new HttpRequestMessage(HttpMethod.Head, uri))
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                lastModified = response.Content.Headers.LastModified;
            }

            return lastModified.HasValue
                ? System.Math.Abs((dateTime - lastModified.Value.DateTime).TotalSeconds) > 2D
                : true;
        }

        /// <summary>
        /// Downloads the specified <see cref="System.Uri" /> as a file.
        /// </summary>
        /// <param name="uri">The uri to download.</param>
        /// <param name="fileName">The file name to download to.</param>
        /// <param name="overwrite">Set to true to overwrite.</param>
        /// <param name="clientFactory">The client factory.</param>
        /// <returns>The task to download the file.</returns>
        public static async Task DownloadAsFileAsync(this System.Uri uri, string fileName, bool overwrite, IHttpClientFactory? clientFactory = null)
        {
            using var client = clientFactory is null
                ? new HttpClient()
                : clientFactory.CreateClient();
            using var response = await client.GetAsync(uri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await response.Content.ReadAsFileAsync(fileName, overwrite).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads the specified <see cref="System.Uri" /> as a string.
        /// </summary>
        /// <param name="uri">The uri to download.</param>
        /// <returns>The task to download the string.</returns>
        /// <param name="clientFactory">The client factory.</param>
        public static async Task<string> DownloadAsStringAsync(this System.Uri uri, IHttpClientFactory? clientFactory = null)
        {
            using var client = clientFactory is null
                ? new HttpClient()
                : clientFactory.CreateClient();
            return await client.GetStringAsync(uri).ConfigureAwait(false);
        }

        private static async Task ReadAsFileAsync(this HttpContent content, string fileName, bool overwrite)
        {
            if (!overwrite && File.Exists(fileName))
            {
                throw new System.InvalidOperationException($"\"{fileName}\" already exists.");
            }

            var pathName = Path.GetFullPath(fileName);
            FileStream? fileStream = default;
            try
            {
                fileStream = new FileStream(pathName, FileMode.Create, FileAccess.Write, FileShare.None);

                await content.CopyToAsync(fileStream).ContinueWith(
                    _ =>
                    {
                        fileStream.Close();
                        var dateTimeOffset = content.Headers.LastModified;
                        if (dateTimeOffset.HasValue)
                        {
                            var fileSystemInfo = new FileInfo(pathName) { LastWriteTimeUtc = dateTimeOffset.Value.UtcDateTime };
                        }
                    },
                    System.Threading.CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default).ConfigureAwait(false);

                fileStream.Dispose();
            }
            catch
            {
                fileStream?.Close();
                throw;
            }
        }
    }
}