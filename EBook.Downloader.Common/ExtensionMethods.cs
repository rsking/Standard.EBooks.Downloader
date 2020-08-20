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
        /// <param name="modifier">The URI modifier.</param>
        /// <returns>Returns <see langword="true"/> if the last modified date does not match; otherwise <see langword="false"/>.</returns>
        public static async Task<(bool download, System.Uri uri)> ShouldDownloadAsync(this System.Uri uri, System.DateTime dateTime, IHttpClientFactory? clientFactory = default, System.Func<System.Uri, System.Uri>? modifier = default)
        {
            using var handler = clientFactory is null
                ? new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None }
                : default(HttpMessageHandler);
            using var client = handler is null && clientFactory is not null
                ? clientFactory.CreateClient("header")
                : new HttpClient(handler);

            var lastModified = await GetDateTimeOffset(uri, client).ConfigureAwait(false);
            if (!lastModified.HasValue && modifier is not null)
            {
                System.Uri updated;
                while ((updated = modifier(uri)) != uri)
                {
                    uri = updated;
                    lastModified = await GetDateTimeOffset(updated, client).ConfigureAwait(false);
                    if (lastModified.HasValue)
                    {
                        break;
                    }
                }
            }

            return lastModified.HasValue
                ? (System.Math.Abs((dateTime - lastModified.Value.DateTime).TotalSeconds) > 2D, uri)
                : default((bool, System.Uri));

            static async Task<System.DateTimeOffset?> GetDateTimeOffset(System.Uri uri, HttpClient client)
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, uri);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                return response.IsSuccessStatusCode
                    ? response.Content.Headers.LastModified
                    : default;
            }
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