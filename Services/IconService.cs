
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace KageDock.Services
{
    public sealed class IconService
    {
        private readonly ConcurrentDictionary<
        string,
        Lazy<Task<BitmapSource?>>> _requests =
            new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _extractionSemaphore = new(3);

        public async Task<BitmapSource?> GetIconAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var request = _requests.GetOrAdd(
                path,
                key => new Lazy<Task<BitmapSource?>>(
            () => Task.Run(async () =>
            {
                await _extractionSemaphore.WaitAsync();

                try
                {
                    return IconExtractorService.GetIcon(key);
                }
                finally
                {
                    _extractionSemaphore.Release();
                }
            }),
            LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                var icon = await request.Value;

                // Only successfully extracted icons stay cached.
                // Null results can be retried later.
                if (icon == null)
                    RemoveRequest(path, request);

                return icon;
            }
            catch
            {
                // Failed requests can also be retried.
                RemoveRequest(path, request);
                throw;
            }
        }

        private void RemoveRequest(
            string path,
            Lazy<Task<BitmapSource?>> request)
        {
            var collection =
                (ICollection<KeyValuePair<
                    string,
                    Lazy<Task<BitmapSource?>>>>)_requests;

            collection.Remove(
                new KeyValuePair<string, Lazy<Task<BitmapSource?>>>(
                    path,
                    request));
        }
    }
}
