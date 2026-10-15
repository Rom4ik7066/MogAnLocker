using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace MogLocker.AdBlock
{
    public static class NativeMessagingHost
    {
        private const int MaximumMessageBytes = 1024 * 1024;
        private static readonly JavaScriptSerializer Serializer =
            new JavaScriptSerializer { MaxJsonLength = MaximumMessageBytes };

        public static bool IsHostInvocation(string[] args)
        {
            return args != null && args.Any(argument =>
                String.Equals(argument, "--native-messaging", StringComparison.Ordinal) ||
                String.Equals(argument, "chrome-extension://" +
                    NativeMessagingRegistration.ExtensionId + "/", StringComparison.Ordinal));
        }

        public static void Run()
        {
            try
            {
                var store = new AdBlockStore();
                var engine = new FilterRuleEngine(store);
                RuleCompileResult cachedRules = null;
                int cachedRevision = -1;
                using (Stream input = Console.OpenStandardInput())
                using (Stream output = Console.OpenStandardOutput())
                {
                    while (true)
                    {
                        byte[] header = new byte[4];
                        int firstByte = input.ReadByte();
                        if (firstByte < 0)
                            return;
                        header[0] = (byte)firstByte;
                        if (!ReadExactly(input, header, 1, 3))
                            throw new EndOfStreamException("Незавершённый заголовок native messaging.");
                        int length = BitConverter.ToInt32(header, 0);
                        if (length < 0 || length > MaximumMessageBytes)
                            throw new InvalidDataException("Размер native messaging сообщения превышает лимит.");
                        byte[] payload = new byte[length];
                        if (!ReadExactly(input, payload, 0, length))
                            throw new EndOfStreamException("Незавершённое native messaging сообщение.");

                        object response;
                        string requestId = null;
                        try
                        {
                            Dictionary<string, object> request =
                                Serializer.DeserializeObject(Encoding.UTF8.GetString(payload)) as
                                    Dictionary<string, object>;
                            if (request == null)
                                throw new InvalidDataException("Сообщение не является JSON-объектом.");
                            requestId = GetString(request, "id");
                            Dictionary<string, object> data =
                                request.ContainsKey("data")
                                    ? request["data"] as Dictionary<string, object>
                                    : new Dictionary<string, object>();
                            if (data == null)
                                data = new Dictionary<string, object>();
                            object result = HandleRequest(
                                store, engine, request, data, ref cachedRules, ref cachedRevision);
                            response = new Dictionary<string, object>
                            {
                                { "id", requestId },
                                { "ok", true },
                                { "data", result }
                            };
                        }
                        catch (Exception ex)
                        {
                            WriteError(store.RootDirectory, ex);
                            response = new Dictionary<string, object>
                            {
                                { "id", requestId },
                                { "ok", false },
                                { "error", ex.Message }
                            };
                        }
                        WriteMessage(output, Serializer.Serialize(response));
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MogLocker", "AdBlock"), ex);
            }
        }

        private static object HandleRequest(
            AdBlockStore store,
            FilterRuleEngine engine,
            Dictionary<string, object> request,
            Dictionary<string, object> data,
            ref RuleCompileResult cachedRules,
            ref int cachedRevision)
        {
            string action = GetString(request, "action");
            switch (action)
            {
                case "getState":
                    AdBlockState currentState = store.Read();
                    RuleCompileResult currentRules =
                        GetCompiledRules(store, engine, ref cachedRules, ref cachedRevision);
                    Dictionary<string, object> publicState = GetPublicState(currentState);
                    publicState["compiledRuleCount"] = currentRules.NetworkRules.Count;
                    publicState["cosmeticRuleCount"] = currentRules.CosmeticRules.Count;
                    publicState["unsupportedRuleCount"] = currentRules.UnsupportedRuleCount;
                    return publicState;
                case "setEnabled":
                    bool enabled = GetBoolean(data, "enabled");
                    return GetPublicState(store.Update(state => state.Enabled = enabled, true));
                case "getRulesPage":
                    RuleCompileResult compiled = GetCompiledRules(store, engine, ref cachedRules, ref cachedRevision);
                    int offset = Math.Max(0, GetInt(data, "offset"));
                    int limit = Math.Max(1, Math.Min(400, GetInt(data, "limit", 300)));
                    return new Dictionary<string, object>
                    {
                        { "total", compiled.NetworkRules.Count },
                        { "rules", compiled.NetworkRules.Skip(offset).Take(limit).ToList() }
                    };
                case "getCosmeticRulesPage":
                    RuleCompileResult cosmetics = GetCompiledRules(store, engine, ref cachedRules, ref cachedRevision);
                    int cosmeticOffset = Math.Max(0, GetInt(data, "offset"));
                    int cosmeticLimit = Math.Max(1, Math.Min(300, GetInt(data, "limit", 200)));
                    return new Dictionary<string, object>
                    {
                        { "total", cosmetics.CosmeticRules.Count },
                        { "rules", FilterRuleEngine.SliceCosmeticRules(
                            cosmetics.CosmeticRules, cosmeticOffset, cosmeticLimit) }
                    };
                case "addRule":
                    return AddRule(store, GetString(data, "rule"));
                case "removeRule":
                    return RemoveRule(store, GetString(data, "rule"));
                case "addAllowlist":
                    return ChangeAllowlist(store, GetString(data, "domain"), true);
                case "removeAllowlist":
                    return ChangeAllowlist(store, GetString(data, "domain"), false);
                case "setFilterEnabled":
                    return SetFilterEnabled(store, GetString(data, "id"), GetBoolean(data, "enabled"));
                case "recordBlocked":
                    return RecordBlocked(store, data);
                case "clearStats":
                    return ClearStatistics(store);
                case "getCosmeticsForHost":
                    return GetCosmeticsForHost(store, engine, data, ref cachedRules, ref cachedRevision);
                default:
                    throw new InvalidDataException("Неизвестная команда AdBlock: " + action);
            }
        }

        private static object GetCosmeticsForHost(
            AdBlockStore store,
            FilterRuleEngine engine,
            Dictionary<string, object> data,
            ref RuleCompileResult cachedRules,
            ref int cachedRevision)
        {
            List<string> host = FilterRuleEngine.NormalizeDomains(GetString(data, "host"));
            if (host.Count == 0)
                return new List<CosmeticRule>();
            string currentHost = host[0];
            RuleCompileResult compiled = GetCompiledRules(store, engine, ref cachedRules, ref cachedRevision);
            List<CosmeticRule> matching = compiled.CosmeticRules.Where(rule =>
                (rule.domains.Count == 0 || rule.domains.Any(domain => DomainMatches(currentHost, domain))) &&
                !rule.excludedDomains.Any(domain => DomainMatches(currentHost, domain))).ToList();
            int offset = Math.Max(0, GetInt(data, "offset"));
            int limit = Math.Max(1, Math.Min(200, GetInt(data, "limit", 150)));
            return new Dictionary<string, object>
            {
                { "total", matching.Count },
                { "rules", matching.Skip(offset).Take(limit).ToList() }
            };
        }

        private static RuleCompileResult GetCompiledRules(
            AdBlockStore store,
            FilterRuleEngine engine,
            ref RuleCompileResult cachedRules,
            ref int cachedRevision)
        {
            AdBlockState state = store.Read();
            if (cachedRules == null || cachedRevision != state.FilterRevision)
            {
                cachedRules = engine.Compile(state);
                cachedRevision = state.FilterRevision;
            }
            return cachedRules;
        }

        private static object AddRule(AdBlockStore store, string rule)
        {
            string validationError;
            if (!FilterRuleEngine.ValidateUserRule(rule, out validationError))
                throw new InvalidDataException(validationError);
            string normalized = rule.Trim();
            AdBlockState state = store.Update(current =>
            {
                if (!current.UserRules.Contains(normalized, StringComparer.OrdinalIgnoreCase) &&
                    current.UserRules.Count < FilterRuleEngine.MaximumUserRules)
                    current.UserRules.Add(normalized);
                else if (!current.UserRules.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("Достигнут лимит пользовательских правил (500).");
            }, true);
            return GetPublicState(state);
        }

        private static object RemoveRule(AdBlockStore store, string rule)
        {
            AdBlockState state = store.Update(current =>
                current.UserRules.RemoveAll(item =>
                    String.Equals(item, rule, StringComparison.OrdinalIgnoreCase)), true);
            return GetPublicState(state);
        }

        private static object ChangeAllowlist(AdBlockStore store, string domain, bool add)
        {
            List<string> normalized = FilterRuleEngine.NormalizeDomains(domain);
            if (normalized.Count == 0)
                throw new InvalidDataException("Укажите допустимый домен, например example.com.");
            string value = normalized[0];
            AdBlockState state = store.Update(current =>
            {
                if (add)
                {
                    if (!current.Allowlist.Contains(value, StringComparer.OrdinalIgnoreCase))
                    {
                        if (current.Allowlist.Count >= FilterRuleEngine.MaximumAllowlistDomains)
                            throw new InvalidDataException("Достигнут лимит исключений Allowlist (200).");
                        current.Allowlist.Add(value);
                    }
                }
                else
                {
                    current.Allowlist.RemoveAll(item =>
                        String.Equals(item, value, StringComparison.OrdinalIgnoreCase));
                }
            }, true);
            return GetPublicState(state);
        }

        private static object SetFilterEnabled(AdBlockStore store, string id, bool enabled)
        {
            AdBlockState state = store.Update(current =>
            {
                FilterListSetting list = current.FilterLists.FirstOrDefault(item => item.Id == id);
                if (list == null)
                    throw new InvalidDataException("Фильтр-лист не найден: " + id);
                list.Enabled = enabled;
            }, true);
            return GetPublicState(state);
        }

        private static object RecordBlocked(AdBlockStore store, Dictionary<string, object> data)
        {
            object rawRecords;
            if (!data.TryGetValue("records", out rawRecords))
                throw new InvalidDataException("В сообщении отсутствует список заблокированных запросов.");
            IEnumerable records = rawRecords as IEnumerable;
            if (records == null)
                throw new InvalidDataException("Список заблокированных запросов имеет неверный формат.");
            var batch = new List<BlockedRequest>();
            foreach (object item in records)
            {
                Dictionary<string, object> row = item as Dictionary<string, object>;
                if (row == null)
                    continue;
                string url = GetString(row, "url");
                Uri uri;
                if (url.Length > 4096 || !Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    continue;
                batch.Add(new BlockedRequest
                {
                    TimeUtc = DateTime.UtcNow,
                    Domain = uri.Host.ToLowerInvariant(),
                    Url = url,
                    ResourceType = GetString(row, "resourceType")
                });
                if (batch.Count >= 100)
                    break;
            }

            AdBlockState updated = store.Update(state =>
            {
                foreach (BlockedRequest blocked in batch)
                {
                    state.BlockedCount++;
                    Increment(state.DomainCounts, blocked.Domain, 5000);
                    Increment(state.ResourceTypeCounts,
                        String.IsNullOrWhiteSpace(blocked.ResourceType) ? "other" : blocked.ResourceType, 100);
                    state.RecentBlocked.Insert(0, blocked);
                }
                if (state.RecentBlocked.Count > 500)
                    state.RecentBlocked.RemoveRange(500, state.RecentBlocked.Count - 500);
            });
            return new Dictionary<string, object> { { "blockedCount", updated.BlockedCount } };
        }

        private static object ClearStatistics(AdBlockStore store)
        {
            store.Update(state =>
            {
                state.BlockedCount = 0;
                state.DomainCounts.Clear();
                state.ResourceTypeCounts.Clear();
                state.RecentBlocked.Clear();
            });
            return new Dictionary<string, object> { { "blockedCount", 0 } };
        }

        private static Dictionary<string, object> GetPublicState(AdBlockState state)
        {
            return new Dictionary<string, object>
            {
                { "revision", state.Revision },
                { "filterRevision", state.FilterRevision },
                { "enabled", state.Enabled },
                { "autoUpdate", state.AutoUpdate },
                { "lastUpdatedUtc", state.LastUpdatedUtc },
                { "lastUpdateAttemptUtc", state.LastUpdateAttemptUtc },
                {
                    "filterLists", state.FilterLists.Select(list => new Dictionary<string, object>
                    {
                        { "id", list.Id },
                        { "name", list.Name },
                        { "url", list.Url },
                        { "enabled", list.Enabled },
                        { "updatedUtc", list.UpdatedUtc },
                        { "lastError", list.LastError }
                    }).ToList()
                },
                { "userRules", state.UserRules },
                { "allowlist", state.Allowlist },
                { "blockedCount", state.BlockedCount },
                {
                    "domainCounts", state.DomainCounts.OrderByDescending(item => item.Value)
                        .Take(100).ToDictionary(item => item.Key, item => item.Value)
                },
                {
                    "resourceTypeCounts", state.ResourceTypeCounts.OrderByDescending(item => item.Value)
                        .Take(20).ToDictionary(item => item.Key, item => item.Value)
                },
                {
                    "recentBlocked", state.RecentBlocked.Take(50).Select(item => new Dictionary<string, object>
                    {
                        { "timeUtc", item.TimeUtc },
                        { "domain", item.Domain },
                        { "url", item.Url.Length > 1024 ? item.Url.Substring(0, 1024) : item.Url },
                        { "resourceType", item.ResourceType }
                    }).ToList()
                }
            };
        }

        private static bool DomainMatches(string host, string domain)
        {
            return String.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
        }

        private static void Increment(Dictionary<string, long> values, string key, int maximumEntries)
        {
            long count;
            if (!values.ContainsKey(key) && values.Count >= maximumEntries)
                return;
            values.TryGetValue(key, out count);
            values[key] = count + 1;
        }

        private static string GetString(Dictionary<string, object> values, string key)
        {
            object value;
            return values.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? String.Empty
                : String.Empty;
        }

        private static int GetInt(Dictionary<string, object> values, string key, int defaultValue = 0)
        {
            object value;
            int result;
            return values.TryGetValue(key, out value) &&
                Int32.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out result)
                ? result
                : defaultValue;
        }

        private static bool GetBoolean(Dictionary<string, object> values, string key)
        {
            object value;
            if (!values.TryGetValue(key, out value) || value == null)
                return false;
            if (value is bool)
                return (bool)value;
            bool result;
            return Boolean.TryParse(Convert.ToString(value), out result) && result;
        }

        private static bool ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, offset + total, count - total);
                if (read == 0)
                    return false;
                total += read;
            }
            return true;
        }

        private static void WriteMessage(Stream output, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            if (payload.Length > MaximumMessageBytes)
                throw new InvalidDataException("Ответ AdBlock превысил допустимый размер native messaging сообщения.");
            byte[] length = BitConverter.GetBytes(payload.Length);
            output.Write(length, 0, length.Length);
            output.Write(payload, 0, payload.Length);
            output.Flush();
        }

        private static void WriteError(string rootDirectory, Exception error)
        {
            try
            {
                Directory.CreateDirectory(rootDirectory);
                string logPath = Path.Combine(rootDirectory, "native-host.log");
                File.AppendAllText(logPath,
                    DateTime.UtcNow.ToString("o") + " " + error + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
