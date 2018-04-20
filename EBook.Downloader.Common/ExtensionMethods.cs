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
        /// Downloads ther specified <see cref="System.Uri" /> as a file.
        /// </summary>
        /// <param name="uri">The uri to download.</param>
        /// <param name="fileName">The file name to download to.</param>
        /// <param name="overwrite">Set to true to overwrite.</param>
        /// <returns>The task to download the file.</returns>
        public static async Task DownloadAsFileAsync(this System.Uri uri, string fileName, bool overwrite)
        {
            using (var client = new HttpClient())
            {
                await client.GetAsync(uri).ContinueWith(requestTask =>
                    {
                        var response = requestTask.Result;
                        response.EnsureSuccessStatusCode();
                        return response.Content.ReadAsFileAsync(fileName, overwrite);
                    }).Unwrap();
            }
        }

        /// <summary>
        /// Downloads the specified <see cref="System.Uri" /> as a string.
        /// </summary>
        /// <param name="uri">The uri to download.</param>
        /// <returns>The task to download the string.</returns>
        public static async Task<string> DownloadAsStringAsync(this System.Uri uri)
        {
            using (var client = new HttpClient())
            {
                return await client.GetAsync(uri).ContinueWith(requestTask =>
                    {
                        var response = requestTask.Result;
                        response.EnsureSuccessStatusCode();
                        return response.Content.ReadAsStringAsync();
                    }).Unwrap();
            }
        }

        private static Task ReadAsFileAsync(this HttpContent content, string fileName, bool overwrite)
        {
            string pathName = Path.GetFullPath(fileName);
            if (!overwrite && File.Exists(fileName))
            {
                throw new System.InvalidOperationException($"File {pathName} already exists.");
            }

            FileStream fileStream = null;
            try
            {
                fileStream = new FileStream(pathName, FileMode.Create, FileAccess.Write, FileShare.None);

                return content.CopyToAsync(fileStream).ContinueWith(copyTask =>
                {
                    fileStream.Close();
                    var dateTimeOffset = content.Headers.LastModified;
                    if (dateTimeOffset.HasValue)
                    {
                        var fileSystemInfo = new FileInfo(pathName);
                        fileSystemInfo.LastWriteTime = dateTimeOffset.Value.DateTime;
                    }
                });
            }
            catch
            {
                if (fileStream != null)
                {
                    fileStream.Close();
                }

                throw;
            }
        }
    }
}