using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MogLocker.AdBlock
{
    public sealed class FilterRuleEngine
    {
        public const int MaximumDynamicRules = 4500;
        public const int MaximumFilterListRules = 3500;
        public const int MaximumCosmeticRules = 2500;
        public const int MaximumUserRules = 500;
        public const int MaximumAllowlistDomains = 200;

        private static readonly Dictionary<string, string> ResourceTypeMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "document", "main_frame" },
                { "subdocument", "sub_frame" },
                { "xmlhttprequest", "xmlhttprequest" },
                { "xhr", "xmlhttprequest" },
                { "script", "script" },
                { "image", "image" },
                { "stylesheet", "stylesheet" },
                { "font", "font" },
                { "media", "media" },
                { "object", "object" },
                { "other", "other" },
                { "ping", "ping" },
                { "websocket", "websocket" }
            };

        private static readonly Regex DomainPattern =
            new Regex(@"^(?:\*\.)?[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly string[] BuiltInAdDomains =
        {
            "doubleclick.net", "googlesyndication.com", "googleadservices.com",
            "adservice.google.com", "adnxs.com", "adform.net", "criteo.com",
            "taboola.com", "outbrain.com", "pubmatic.com", "openx.net",
            "rubiconproject.com", "smartadserver.com", "adsrvr.org", "scorecardresearch.com"
        };

        private readonly AdBlockStore store;

        public FilterRuleEngine(AdBlockStore store)
        {
            if (store == null)
                throw new ArgumentNullException("store");
            this.store = store;
        }

        public RuleCompileResult Compile(AdBlockState state)
        {
            var result = new RuleCompileResult();
            if (state == null || !state.Enabled)
                return result;

            foreach (string domain in state.Allowlist.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                if (result.NetworkRules.Count + 2 > MaximumDynamicRules)
                    break;
                List<string> domains = NormalizeDomains(domain);
                if (domains.Count == 0)
                    continue;
                result.NetworkRules.Add(new BrowserRule
                {
                    id = result.NetworkRules.Count + 1,
                    priority = 10000,
                    action = new Dictionary<string, object> { { "type", "allow" } },
                    condition = new Dictionary<string, object> { { "requestDomains", domains } }
                });
                result.NetworkRules.Add(new BrowserRule
                {
                    id = result.NetworkRules.Count + 1,
                    priority = 10000,
                    action = new Dictionary<string, object> { { "type", "allow" } },
                    condition = new Dictionary<string, object> { { "initiatorDomains", domains } }
                });
                if (result.NetworkRules.Count >= MaximumDynamicRules)
                    break;
            }

            List<FilterListSetting> enabledLists = state.FilterLists
                .Where(item => item.Enabled &&
                    !String.Equals(item.Id, "builtin", StringComparison.OrdinalIgnoreCase))
                .ToList();
            int baseListQuota = enabledLists.Count == 0
                ? 0
                : MaximumFilterListRules / enabledLists.Count;
            int extraListRules = enabledLists.Count == 0
                ? 0
                : MaximumFilterListRules % enabledLists.Count;
            for (int index = 0; index < enabledLists.Count; index++)
            {
                FilterListSetting list = enabledLists[index];
                string content;
                try
                {
                    content = store.ReadFilterCache(list.Id);
                }
                catch (IOException)
                {
                    content = String.Empty;
                }
                int listQuota = baseListQuota + (index < extraListRules ? 1 : 0);
                CompileText(content, result, result.NetworkRules.Count + listQuota, false);
            }

            bool builtInEnabled = state.FilterLists.Any(item => item.Enabled &&
                String.Equals(item.Id, "builtin", StringComparison.OrdinalIgnoreCase));
            if (builtInEnabled)
            {
                foreach (string domain in BuiltInAdDomains)
                {
                    string filter = "||" + domain + "^";
                    if (result.NetworkRules.Count >= MaximumDynamicRules ||
                        result.NetworkRules.Any(rule =>
                        {
                            object existing;
                            return rule.condition.TryGetValue("urlFilter", out existing) &&
                                String.Equals(Convert.ToString(existing), filter,
                                    StringComparison.OrdinalIgnoreCase);
                        }))
                        continue;
                    BrowserRule builtIn;
                    if (TryCompileNetworkRule(filter, result.NetworkRules.Count + 1, false, out builtIn))
                    {
                        builtIn.id = result.NetworkRules.Count + 1;
                        result.NetworkRules.Add(builtIn);
                    }
                }
            }

            CompileText(String.Join(Environment.NewLine, state.UserRules), result,
                MaximumDynamicRules, true);
            ApplyCosmeticExceptions(result);
            return result;
        }

        public static bool ValidateUserRule(string input, out string error)
        {
            error = null;
            if (String.IsNullOrWhiteSpace(input) || input.Trim().Length > 512)
            {
                error = "Правило пустое или длиннее 512 символов.";
                return false;
            }

            string rule = input.Trim();
            if (rule.IndexOf("##", StringComparison.Ordinal) >= 0 ||
                rule.IndexOf("#@#", StringComparison.Ordinal) >= 0)
            {
                int exceptionDelimiter = rule.IndexOf("#@#", StringComparison.Ordinal);
                int delimiter = exceptionDelimiter >= 0
                    ? exceptionDelimiter
                    : rule.IndexOf("##", StringComparison.Ordinal);
                int markerLength = exceptionDelimiter >= 0 ? 3 : 2;
                if (rule.IndexOf("#?#", StringComparison.Ordinal) >= 0 ||
                    !IsSafeSelector(rule.Substring(delimiter + markerLength).Trim()))
                {
                    error = "Косметическое правило содержит неподдерживаемый или небезопасный селектор.";
                    return false;
                }
                string domainPart = rule.Substring(0, delimiter);
                if (exceptionDelimiter >= 0 && domainPart.Split(',')
                    .Any(domain => domain.Trim().StartsWith("~", StringComparison.Ordinal)))
                {
                    error = "Исключения косметических правил с отрицательными доменами не поддерживаются.";
                    return false;
                }
                foreach (string rawDomain in domainPart.Split(new[] { ',' },
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    string domain = rawDomain.Trim().TrimStart('~');
                    if (NormalizeDomains(domain).Count == 0)
                    {
                        error = "В косметическом правиле указан недопустимый домен.";
                        return false;
                    }
                }
                return true;
            }

            BrowserRule compiled;
            if (!TryCompileNetworkRule(rule, 1, true, out compiled))
            {
                error = "Правило не поддерживается. Используйте ABP-шаблон, например ||ads.example^$script.";
                return false;
            }
            return true;
        }

        public static List<CosmeticRule> SliceCosmeticRules(
            IList<CosmeticRule> rules, int offset, int limit)
        {
            if (rules == null)
                return new List<CosmeticRule>();
            return rules.Skip(Math.Max(0, offset)).Take(Math.Max(0, Math.Min(limit, 500))).ToList();
        }

        public static List<string> NormalizeDomains(string input)
        {
            var result = new List<string>();
            if (String.IsNullOrWhiteSpace(input))
                return result;
            string candidate = input.Trim();
            Uri uri;
            if (Uri.TryCreate(candidate.Contains("://") ? candidate : "https://" + candidate,
                UriKind.Absolute, out uri))
                candidate = uri.Host;
            candidate = candidate.Trim().TrimEnd('.').ToLowerInvariant();
            if (candidate.StartsWith("*.", StringComparison.Ordinal))
                candidate = candidate.Substring(2);
            if (candidate.Length == 0 || !DomainPattern.IsMatch(candidate) ||
                candidate.Contains("..") || candidate.Contains("/") || candidate.Contains(":"))
                return result;
            result.Add(candidate);
            return result;
        }

        private void CompileText(
            string text, RuleCompileResult result, int networkRuleLimit, bool isUserRules)
        {
            if (String.IsNullOrEmpty(text))
                return;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string rule = line.Trim();
                    if (rule.Length == 0 || rule.StartsWith("!", StringComparison.Ordinal) ||
                        rule.StartsWith("[", StringComparison.Ordinal))
                        continue;

                    if (TryCompileCosmetic(rule, result))
                        continue;
                    if (rule.IndexOf("##", StringComparison.Ordinal) >= 0 ||
                        rule.IndexOf("#@#", StringComparison.Ordinal) >= 0 ||
                        rule.IndexOf("#?#", StringComparison.Ordinal) >= 0)
                    {
                        result.UnsupportedRuleCount++;
                        continue;
                    }

                    BrowserRule browserRule;
                    if (!TryCompileNetworkRule(rule, result.NetworkRules.Count + 1, isUserRules, out browserRule))
                    {
                        result.UnsupportedRuleCount++;
                        continue;
                    }
                    if (result.NetworkRules.Count >= networkRuleLimit)
                        continue;
                    browserRule.id = result.NetworkRules.Count + 1;
                    result.NetworkRules.Add(browserRule);
                }
            }
        }

        private bool TryCompileCosmetic(string rule, RuleCompileResult result)
        {
            int exceptionDelimiter = rule.IndexOf("#@#", StringComparison.Ordinal);
            bool isException = exceptionDelimiter >= 0;
            int delimiter = isException ? exceptionDelimiter : rule.IndexOf("##", StringComparison.Ordinal);
            if (delimiter < 0)
                return false;
            if (rule.IndexOf("#?#", StringComparison.Ordinal) >= 0 ||
                (isException
                    ? result.CosmeticExceptions.Count >= MaximumCosmeticRules
                    : result.CosmeticRules.Count >= MaximumCosmeticRules))
            {
                result.UnsupportedRuleCount++;
                return true;
            }

            string domainsPart = rule.Substring(0, delimiter);
            string selector = rule.Substring(delimiter + (isException ? 3 : 2)).Trim();
            if (!IsSafeSelector(selector))
            {
                result.UnsupportedRuleCount++;
                return true;
            }

            var included = new List<string>();
            var excluded = new List<string>();
            if (domainsPart.Length > 0)
            {
                foreach (string item in domainsPart.Split(','))
                {
                    string domain = item.Trim();
                    bool exclude = domain.StartsWith("~", StringComparison.Ordinal);
                    if (exclude)
                        domain = domain.Substring(1);
                    List<string> normalized = NormalizeDomains(domain);
                    if (normalized.Count == 0)
                    {
                        result.UnsupportedRuleCount++;
                        return true;
                    }
                    (exclude ? excluded : included).AddRange(normalized);
                }
            }

            if (isException && excluded.Count > 0)
            {
                result.UnsupportedRuleCount++;
                return true;
            }

            var cosmeticRule = new CosmeticRule
            {
                domains = included,
                excludedDomains = excluded,
                selector = selector
            };
            if (isException)
                result.CosmeticExceptions.Add(cosmeticRule);
            else
                result.CosmeticRules.Add(cosmeticRule);
            return true;
        }

        private static void ApplyCosmeticExceptions(RuleCompileResult result)
        {
            foreach (CosmeticRule exception in result.CosmeticExceptions.Where(rule =>
                rule.domains.Count == 0))
            {
                result.CosmeticRules.RemoveAll(rule =>
                    String.Equals(rule.selector, exception.selector, StringComparison.Ordinal));
            }

            foreach (CosmeticRule exception in result.CosmeticExceptions.Where(rule =>
                rule.domains.Count > 0))
            {
                foreach (CosmeticRule rule in result.CosmeticRules.Where(item =>
                    String.Equals(item.selector, exception.selector, StringComparison.Ordinal)))
                    rule.excludedDomains.AddRange(exception.domains);
            }
            result.CosmeticExceptions.Clear();
        }

        private static bool IsSafeSelector(string selector)
        {
            return !String.IsNullOrWhiteSpace(selector) &&
                selector.Length <= 512 &&
                selector.IndexOf('{') < 0 &&
                selector.IndexOf('}') < 0;
        }

        private static bool TryCompileNetworkRule(
            string text, int id, bool userRule, out BrowserRule result)
        {
            result = null;
            bool allow = text.StartsWith("@@", StringComparison.Ordinal);
            string rule = allow ? text.Substring(2) : text;
            string pattern = rule;
            string options = null;
            int optionIndex = FindOptionsDelimiter(rule);
            if (optionIndex >= 0)
            {
                pattern = rule.Substring(0, optionIndex);
                options = rule.Substring(optionIndex + 1);
            }
            if (pattern.Length == 0 || pattern.StartsWith("/", StringComparison.Ordinal) ||
                pattern.IndexOf('|') >= 0 && !(pattern.StartsWith("|", StringComparison.Ordinal)))
                return false;

            var condition = new Dictionary<string, object>();
            string urlFilter = pattern;
            if (urlFilter.Length == 0 || urlFilter.Length > 2048 ||
                urlFilter.Any(Char.IsWhiteSpace) ||
                urlFilter.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                return false;
            int firstBar = urlFilter.IndexOf('|');
            int lastBar = urlFilter.LastIndexOf('|');
            if (firstBar >= 0)
            {
                bool leadingAnchor = firstBar == 0 &&
                    (lastBar == 0 || lastBar == urlFilter.Length - 1);
                bool domainAnchor = firstBar == 0 && lastBar == 1 &&
                    urlFilter.StartsWith("||", StringComparison.Ordinal);
                bool trailingAnchor = firstBar == urlFilter.Length - 1 && lastBar == firstBar;
                if (!leadingAnchor && !domainAnchor && !trailingAnchor)
                    return false;
            }
            condition["urlFilter"] = urlFilter;

            var resourceTypes = new List<string>();
            var excludedResourceTypes = new List<string>();
            List<string> initiators = new List<string>();
            List<string> excludedInitiators = new List<string>();
            string domainType = null;
            bool caseSensitive = false;
            bool important = false;

            if (!String.IsNullOrEmpty(options))
            {
                foreach (string rawOption in options.Split(','))
                {
                    string option = rawOption.Trim();
                    if (option.Length == 0)
                        continue;
                    bool exclude = option.StartsWith("~", StringComparison.Ordinal);
                    string value = exclude ? option.Substring(1) : option;
                    int equals = value.IndexOf('=');
                    string name = equals < 0 ? value : value.Substring(0, equals);
                    string argument = equals < 0 ? String.Empty : value.Substring(equals + 1);

                    string resourceType;
                    if (ResourceTypeMap.TryGetValue(name, out resourceType) && equals < 0)
                    {
                        (exclude ? excludedResourceTypes : resourceTypes).Add(resourceType);
                    }
                    else if (String.Equals(name, "domain", StringComparison.OrdinalIgnoreCase) && equals >= 0)
                    {
                        foreach (string domain in argument.Split('|'))
                        {
                            bool domainExcluded = domain.StartsWith("~", StringComparison.Ordinal);
                            string item = domainExcluded ? domain.Substring(1) : domain;
                            List<string> normalized = NormalizeDomains(item);
                            if (normalized.Count == 0)
                                return false;
                            (domainExcluded ? excludedInitiators : initiators).AddRange(normalized);
                        }
                    }
                    else if ((String.Equals(name, "third-party", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(name, "3p", StringComparison.OrdinalIgnoreCase)) && equals < 0)
                    {
                        if (exclude)
                            domainType = "firstParty";
                        else
                            domainType = "thirdParty";
                    }
                    else if (String.Equals(name, "match-case", StringComparison.OrdinalIgnoreCase) && equals < 0)
                    {
                        caseSensitive = !exclude;
                    }
                    else if (String.Equals(name, "important", StringComparison.OrdinalIgnoreCase) && equals < 0)
                    {
                        important = !exclude;
                    }
                    else if (String.Equals(name, "badfilter", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(name, "popup", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(name, "redirect", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(name, "redirect-rule", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(name, "removeparam", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(name, "csp", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(name, "replace", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(name, "header", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            if (resourceTypes.Count > 0 && excludedResourceTypes.Count > 0)
                return false;
            if (resourceTypes.Count > 0)
                condition["resourceTypes"] = resourceTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (excludedResourceTypes.Count > 0)
                condition["excludedResourceTypes"] = excludedResourceTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (initiators.Count > 0)
                condition["initiatorDomains"] = initiators.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (excludedInitiators.Count > 0)
                condition["excludedInitiatorDomains"] = excludedInitiators.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (domainType != null)
                condition["domainType"] = domainType;
            if (caseSensitive)
                condition["isUrlFilterCaseSensitive"] = true;

            result = new BrowserRule
            {
                id = id,
                priority = allow ? 100 : (important ? 1000 : (userRule ? 10 : 1)),
                action = new Dictionary<string, object> { { "type", allow ? "allow" : "block" } },
                condition = condition
            };
            return true;
        }

        private static int FindOptionsDelimiter(string rule)
        {
            bool escaped = false;
            for (int i = 0; i < rule.Length; i++)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (rule[i] == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (rule[i] == '$')
                    return i;
            }
            return -1;
        }
    }
}
