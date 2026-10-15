using MogLocker.AdBlock;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class AdBlockEngineSmokeTests
{
    private static int assertions;

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "MogLocker-AdBlock-Smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var store = new AdBlockStore(root);
            AdBlockState state = store.Read();
            store.WriteFilterCache("easylist",
                "! test list\r\n||ads.example^\r\n@@||safe.ads.example^$script\r\n" +
                "example.com##.sponsored\r\nads.example.com#@#.sponsored\r\n" +
                "##.generic\r\n#@#.generic\r\n" +
                "||unsupported.example^$popup\r\n");
            store.WriteFilterCache("easyprivacy",
                "||tracker.example^$xmlhttprequest,third-party\r\n");
            state = store.Update(current =>
            {
                current.Enabled = true;
                current.FilterLists = AdBlockStore.CreateDefaultFilterLists();
                current.UserRules.Add("||custom-ad.example^$image");
                current.Allowlist.Add("trusted.example");
            }, true);

            var engine = new FilterRuleEngine(store);
            RuleCompileResult compiled = engine.Compile(state);
            Assert(compiled.NetworkRules.Any(rule =>
                rule.condition.ContainsKey("urlFilter") &&
                Equals(rule.condition["urlFilter"], "||ads.example^") &&
                Equals(rule.action["type"], "block")), "ABP domain rule compiles to a blocking DNR rule.");
            Assert(compiled.NetworkRules.Any(rule =>
                rule.condition.ContainsKey("urlFilter") &&
                Equals(rule.condition["urlFilter"], "||tracker.example^") &&
                ((List<string>)rule.condition["resourceTypes"]).Contains("xmlhttprequest") &&
                Equals(rule.condition["domainType"], "thirdParty")),
                "ABP request types and third-party option are preserved.");
            Assert(compiled.NetworkRules.Any(rule =>
                rule.condition.ContainsKey("urlFilter") &&
                Equals(rule.condition["urlFilter"], "||doubleclick.net^") &&
                Equals(rule.action["type"], "block")),
                "Built-in ad domains remain available without relying on downloaded lists.");
            Assert(compiled.NetworkRules.Any(rule =>
                rule.condition.ContainsKey("urlFilter") &&
                Equals(rule.condition["urlFilter"], "||safe.ads.example^") &&
                Equals(rule.action["type"], "allow")),
                "ABP exception compiles to an allow rule.");
            Assert(compiled.NetworkRules.Any(rule =>
                rule.condition.ContainsKey("urlFilter") &&
                Equals(rule.condition["urlFilter"], "||custom-ad.example^") &&
                ((List<string>)rule.condition["resourceTypes"]).Contains("image")),
                "User rules compile to dynamic browser rules.");
            Assert(compiled.NetworkRules.Count(rule => rule.priority == 10000) == 2,
                "Allowlist rules cover destination navigation and requests initiated by the allowlisted site.");
            Assert(compiled.CosmeticRules.Any(rule =>
                rule.domains.Contains("example.com") && rule.selector == ".sponsored" &&
                rule.excludedDomains.Contains("ads.example.com")),
                "Domain-scoped cosmetic rules and their exceptions are parsed.");
            Assert(!compiled.CosmeticRules.Any(rule => rule.selector == ".generic"),
                "Global cosmetic exceptions suppress matching selectors.");
            Assert(compiled.UnsupportedRuleCount == 1,
                "Unsupported filter-list syntax is counted for the UI instead of being silently treated as active.");
            Assert(FilterRuleEngine.NormalizeDomains("https://www.example.com/path").Single() ==
                "www.example.com", "Allowlist URLs normalize to a host.");

            string error;
            Assert(FilterRuleEngine.ValidateUserRule("||my-ad.example^$script", out error),
                "Supported custom network rule passes validation.");
            Assert(FilterRuleEngine.ValidateUserRule("example.com##.advert", out error),
                "Supported cosmetic user rule passes validation.");
            Assert(!FilterRuleEngine.ValidateUserRule("||bad.example^$popup", out error),
                "Unsupported browser actions are rejected rather than silently approximated.");
            Assert(!FilterRuleEngine.ValidateUserRule("bad host", out error),
                "Invalid custom filter text is rejected.");

            AdBlockState persisted = new AdBlockStore(root).Read();
            Assert(persisted.UserRules.Contains("||custom-ad.example^$image"),
                "Settings persist across store instances.");
            Assert(persisted.FilterRevision > 0, "Rule edits advance the filter revision.");
            Console.WriteLine("PASS: " + assertions + " AdBlock engine assertions.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void Assert(bool condition, string description)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
