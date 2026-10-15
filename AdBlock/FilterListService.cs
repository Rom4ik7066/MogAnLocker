using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MogLocker.AdBlock
{
    public sealed class FilterRefreshResult
    {
        public int UpdatedCount { get; set; }
        public int FailedCount { get; set; }
        public string Message { get; set; }
    }

    public sealed class FilterListService
    {
        private const int MaximumListBytes = 25 * 1024 * 1024;
        private readonly AdBlockStore store;

        public FilterListService(AdBlockStore store)
        {
            if (store == null)
                throw new ArgumentNullException("store");
            this.store = store;
        }

        public Task<FilterRefreshResult> RefreshAsync(CancellationToken cancellationToken)
        {
            return Task.Run(delegate
            {
                return Refresh(cancellationToken);
            }, cancellationToken);
        }

        public FilterRefreshResult Refresh(CancellationToken cancellationToken)
        {
            AdBlockState initial = store.Read();
            var result = new FilterRefreshResult();
            store.Update(state => state.LastUpdateAttemptUtc = DateTime.UtcNow);

            foreach (FilterListSetting list in initial.FilterLists.Where(item =>
                item.Enabled && !String.IsNullOrWhiteSpace(item.Url)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    string content = DownloadList(list.Url, cancellationToken);
                    if (String.IsNullOrWhiteSpace(content))
                        throw new InvalidDataException("Список фильтров пуст.");

                    store.WriteFilterCache(list.Id, content);
                    DateTime updated = DateTime.UtcNow;
                    store.Update(state =>
                    {
                        FilterListSetting current = state.FilterLists.FirstOrDefault(item => item.Id == list.Id);
                        if (current != null)
                        {
                            current.UpdatedUtc = updated;
                            current.LastError = null;
                        }
                    }, true);
                    result.UpdatedCount++;
                }
                catch (WebException ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);
                    RecordListFailure(list.Id, ex.Message);
                    result.FailedCount++;
                }
                catch (IOException ex)
                {
                    RecordListFailure(list.Id, ex.Message);
                    result.FailedCount++;
                }
                catch (UnauthorizedAccessException ex)
                {
                    RecordListFailure(list.Id, ex.Message);
                    result.FailedCount++;
                }
                catch (InvalidDataException ex)
                {
                    RecordListFailure(list.Id, ex.Message);
                    result.FailedCount++;
                }
            }

            if (result.UpdatedCount > 0)
                store.Update(state => state.LastUpdatedUtc = DateTime.UtcNow);
            result.Message = String.Format(
                "Обновлено списков: {0}; ошибок: {1}. Кэшированные фильтры сохранены при ошибках загрузки.",
                result.UpdatedCount, result.FailedCount);
            return result;
        }

        private static string DownloadList(string url, CancellationToken cancellationToken)
        {
            Uri address;
            if (!Uri.TryCreate(url, UriKind.Absolute, out address) ||
                (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
                throw new InvalidDataException("У фильтр-листа указан недопустимый HTTP(S)-адрес.");

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(address);
            request.Method = "GET";
            request.UserAgent = "MogLocker-AdBlock/1.0 (filter-list updater)";
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (cancellationToken.Register(delegate { request.Abort(); }))
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new WebException("HTTP " + (int)response.StatusCode + " " + response.StatusDescription);
                if (response.ContentLength > MaximumListBytes)
                    throw new InvalidDataException("Размер фильтр-листа превышает 25 МБ.");

                Encoding encoding = Encoding.UTF8;
                if (!String.IsNullOrWhiteSpace(response.CharacterSet))
                {
                    try
                    {
                        encoding = Encoding.GetEncoding(response.CharacterSet.Trim('"'));
                    }
                    catch (ArgumentException)
                    {
                        encoding = Encoding.UTF8;
                    }
                }

                using (Stream input = response.GetResponseStream())
                using (var output = new MemoryStream())
                {
                    byte[] buffer = new byte[32768];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (output.Length + read > MaximumListBytes)
                            throw new InvalidDataException("Размер фильтр-листа превышает 25 МБ.");
                        output.Write(buffer, 0, read);
                    }
                    return encoding.GetString(output.ToArray()).TrimStart('\uFEFF');
                }
            }
        }

        private void RecordListFailure(string listId, string message)
        {
            store.Update(state =>
            {
                FilterListSetting current = state.FilterLists.FirstOrDefault(item => item.Id == listId);
                if (current != null)
                    current.LastError = message;
            });
        }
    }
}
