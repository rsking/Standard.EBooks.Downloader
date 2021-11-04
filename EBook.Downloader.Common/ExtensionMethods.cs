// -----------------------------------------------------------------------
// <copyright file="ExtensionMethods.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Common;

/// <summary>
/// Extension methods.
/// </summary>
public static class ExtensionMethods
{
    /// <summary>
    /// Checks to see whether the specified <see cref="Uri" /> needs to be downloaded based on the last modified date.
    /// </summary>
    /// <param name="uri">The uri to check.</param>
    /// <param name="dateTime">The last modified date time.</param>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="modifier">The URI modifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Returns the download URI if the last modified date does not match; otherwise <see langword="null"/>.</returns>
    public static async Task<Uri?> ShouldDownloadAsync(this Uri uri, DateTime dateTime, IHttpClientFactory? clientFactory = default, Func<Uri, Uri>? modifier = default, CancellationToken cancellationToken = default)
    {
        using var handler = clientFactory is null
            ? new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None }
            : default(HttpMessageHandler);
        using var client = handler is null && clientFactory is not null
            ? clientFactory.CreateClient("header")
            : new HttpClient(handler);

        var lastModified = await GetDateTimeOffsetAsync(uri, client, cancellationToken).ConfigureAwait(false);
        if (!lastModified.HasValue && modifier is not null)
        {
            Uri updated;
            while ((updated = modifier(uri)) != uri)
            {
                uri = updated;
                lastModified = await GetDateTimeOffsetAsync(uri, client, cancellationToken).ConfigureAwait(false);
                if (lastModified.HasValue)
                {
                    break;
                }
            }
        }

        return lastModified.HasValue && System.Math.Abs((dateTime - lastModified.Value.DateTime).TotalSeconds) > 2D
            ? uri
            : default;

        static async Task<DateTimeOffset?> GetDateTimeOffsetAsync(Uri uri, HttpClient client, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? response.Content.Headers.LastModified
                : default;
        }
    }

    /// <summary>
    /// Downloads the specified <see cref="Uri" /> as a file.
    /// </summary>
    /// <param name="uri">The uri to download.</param>
    /// <param name="fileName">The file name to download to.</param>
    /// <param name="overwrite">Set to true to overwrite.</param>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task to download the file.</returns>
    public static async Task DownloadAsFileAsync(this Uri uri, string fileName, bool overwrite, IHttpClientFactory? clientFactory = null, CancellationToken cancellationToken = default)
    {
        using var client = clientFactory is null
            ? new HttpClient()
            : clientFactory.CreateClient();
        using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await response.Content.ReadAsFileAsync(fileName, overwrite, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads the specified <see cref="Uri" /> as a string.
    /// </summary>
    /// <param name="uri">The uri to download.</param>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task to download the string.</returns>
    public static async Task<string> DownloadAsStringAsync(this Uri uri, IHttpClientFactory? clientFactory = null, CancellationToken cancellationToken = default)
    {
        using var client = clientFactory is null
            ? new HttpClient()
            : clientFactory.CreateClient();
        var value = await client.GetStringAsync(uri).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return value;
    }

    /// <summary>
    /// Determines whether the collect is a <see cref="EpubCollectionType.Series"/>.
    /// </summary>
    /// <param name="collection">The collection.</param>
    /// <param name="forcedSeries">The list of <see cref="EpubCollectionType.Set"/> that should be considered to be a <see cref="EpubCollectionType.Series"/>.</param>
    /// <returns><see langword="true"/> if the collection a <see cref="EpubCollectionType.Series"/>; otherwise <see langword="false"/>.</returns>
    public static bool IsSeries(this EpubCollection collection, IList<string> forcedSeries) => collection.Type switch
    {
        EpubCollectionType.Series => true,
        EpubCollectionType.Set => forcedSeries.IndexOf(collection.Name) >= 0,
        _ => false,
    };

    /// <summary>
    /// Determines whether the collect is a <see cref="EpubCollectionType.Set"/>.
    /// </summary>
    /// <param name="collection">The collection.</param>
    /// <param name="forcedSeries">The list of <see cref="EpubCollectionType.Set"/> that should be considered to be a <see cref="EpubCollectionType.Series"/>.</param>
    /// <returns><see langword="true"/> if the collection a <see cref="EpubCollectionType.Set"/>; otherwise <see langword="false"/>.</returns>
    public static bool IsSet(this EpubCollection collection, IList<string> forcedSeries) => collection.Type switch
    {
        EpubCollectionType.Set => forcedSeries.IndexOf(collection.Name) < 0,
        _ => false,
    };

    private static async Task ReadAsFileAsync(this HttpContent content, string fileName, bool overwrite, CancellationToken cancellationToken = default)
    {
        if (!overwrite && File.Exists(fileName))
        {
            throw new InvalidOperationException($"\"{fileName}\" already exists.");
        }

        var pathName = Path.GetFullPath(fileName);
        FileStream? fileStream = default;
        try
        {
            fileStream = new FileStream(pathName, FileMode.Create, FileAccess.Write, FileShare.None);

            await content.CopyToAsync(fileStream).ContinueWith(
                _ =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

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
            DeleteFile();
            throw;
        }

        void DeleteFile()
        {
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }
        }
    }
}
