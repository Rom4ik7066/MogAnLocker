using System;
using System.Collections.Generic;

namespace MogLocker.AdBlock
{
    public sealed class AdBlockState
    {
        public int Revision { get; set; }
        public int FilterRevision { get; set; }
        public bool Enabled { get; set; }
        public bool AutoUpdate { get; set; }
        public DateTime? LastUpdatedUtc { get; set; }
        public DateTime? LastUpdateAttemptUtc { get; set; }
        public List<FilterListSetting> FilterLists { get; set; }
        public List<string> UserRules { get; set; }
        public List<string> Allowlist { get; set; }
        public long BlockedCount { get; set; }
        public Dictionary<string, long> DomainCounts { get; set; }
        public Dictionary<string, long> ResourceTypeCounts { get; set; }
        public List<BlockedRequest> RecentBlocked { get; set; }

        public AdBlockState()
        {
            Enabled = true;
            AutoUpdate = true;
            FilterLists = new List<FilterListSetting>();
            UserRules = new List<string>();
            Allowlist = new List<string>();
            DomainCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            ResourceTypeCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            RecentBlocked = new List<BlockedRequest>();
        }
    }

    public sealed class FilterListSetting
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public bool Enabled { get; set; }
        public DateTime? UpdatedUtc { get; set; }
        public string LastError { get; set; }

        public override string ToString()
        {
            return String.IsNullOrEmpty(LastError) ? Name : Name + " (ошибка: " + LastError + ")";
        }
    }

    public sealed class BlockedRequest
    {
        public DateTime TimeUtc { get; set; }
        public string Domain { get; set; }
        public string Url { get; set; }
        public string ResourceType { get; set; }
    }

    public sealed class BrowserRule
    {
        public int id { get; set; }
        public int priority { get; set; }
        public Dictionary<string, object> action { get; set; }
        public Dictionary<string, object> condition { get; set; }
    }

    public sealed class CosmeticRule
    {
        public List<string> domains { get; set; }
        public List<string> excludedDomains { get; set; }
        public string selector { get; set; }
    }

    public sealed class RuleCompileResult
    {
        public List<BrowserRule> NetworkRules { get; set; }
        public List<CosmeticRule> CosmeticRules { get; set; }
        public List<CosmeticRule> CosmeticExceptions { get; set; }
        public int UnsupportedRuleCount { get; set; }

        public RuleCompileResult()
        {
            NetworkRules = new List<BrowserRule>();
            CosmeticRules = new List<CosmeticRule>();
            CosmeticExceptions = new List<CosmeticRule>();
        }
    }
}
