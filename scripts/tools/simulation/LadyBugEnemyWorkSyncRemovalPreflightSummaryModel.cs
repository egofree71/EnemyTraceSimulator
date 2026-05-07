using System;
using System.Globalization;

/// <summary>
/// v0.7.15 preflight for removing the reference-sync bridge around EnemyWork
/// rejectedMask / fallback helper.
///
/// This class is deliberately conservative. It does not run gameplay logic and it
/// does not alter the comparison timeline. It reads the already-generated
/// source-first Enemy_UpdateOne shadow summary and turns the important facts into
/// an explicit yes/no milestone:
///
/// - the full Enemy_UpdateOne shadow used the exact-PC aligned preferred provider;
/// - the shadow matched all active one-enemy updates;
/// - the modeled comparison included rejectedMask, 0x61C2/fallback helper,
///   tempDir, tempX, and tempY;
/// - the remaining visible simulation is still reference-assisted.
///
/// In other words, this is a gate before trying a later patch where
/// SimulationState carries modeled rejectedMask / 0x61C2 in a shadow branch.
/// </summary>
public static class LadyBugEnemyWorkSyncRemovalPreflightSummaryModel
{
    private const string Version = "v0.7.15";

    public static string BuildSummary(string enemyUpdateOneShadowSummary)
    {
        if (string.IsNullOrWhiteSpace(enemyUpdateOneShadowSummary))
        {
            return "Lady Bug EnemyWork sync-removal preflight " + Version +
                   ": updateOneSummaryFound=false, canTryRejectedFallbackUnsyncedShadow=false" +
                   ". NOTE: no Enemy_UpdateOne shadow summary was available.";
        }

        int checks = ExtractInt(enemyUpdateOneShadowSummary, "checks=");
        int matches = ExtractInt(enemyUpdateOneShadowSummary, "matches=");
        int mismatches = ExtractInt(enemyUpdateOneShadowSummary, "mismatches=");
        int preferredProviderChecks = ExtractInt(enemyUpdateOneShadowSummary, "preferredProviderChecks=");
        int preferredProviderMatchesReference = ExtractInt(enemyUpdateOneShadowSummary, "preferredProviderMatchesReference=");
        int preferredProviderDiffersFromReference = ExtractInt(enemyUpdateOneShadowSummary, "preferredProviderDiffersFromReference=");
        int preferredProviderSkips = ExtractInt(enemyUpdateOneShadowSummary, "preferredProviderSkips=");
        int fallbackSelected = ExtractInt(enemyUpdateOneShadowSummary, "fallbackSelected=");
        int fallbackNotFound = ExtractInt(enemyUpdateOneShadowSummary, "fallbackNotFound=");
        int preferredRejectedCurrentKept = ExtractInt(enemyUpdateOneShadowSummary, "preferredRejectedCurrentKept=");
        int forcedReversalSet = ExtractInt(enemyUpdateOneShadowSummary, "forcedReversalSet=");
        int forcedReversalClear = ExtractInt(enemyUpdateOneShadowSummary, "forcedReversalClear=");
        int skippedMissingVram = ExtractInt(enemyUpdateOneShadowSummary, "skippedMissingVram=");
        int tileAddressOutOfRange = ExtractInt(enemyUpdateOneShadowSummary, "tileAddressOutOfRange=");

        bool exactProviderUsable = ExtractBool(enemyUpdateOneShadowSummary, "preferredExactProviderUsable=");
        int exactTupleMismatches = ExtractInt(enemyUpdateOneShadowSummary, "preferredExactProviderBestWindowTupleMismatches=");
        string providerMode = ExtractValue(enemyUpdateOneShadowSummary, "preferredProviderMode=");

        bool fullShadowClean =
            checks > 0 &&
            matches == checks &&
            mismatches == 0 &&
            skippedMissingVram == 0 &&
            tileAddressOutOfRange == 0;

        bool preferredInputClean =
            providerMode == "exact-PC-aligned" &&
            exactProviderUsable &&
            exactTupleMismatches == 0 &&
            preferredProviderChecks == checks &&
            preferredProviderMatchesReference == checks &&
            preferredProviderDiffersFromReference == 0 &&
            preferredProviderSkips == 0;

        bool currentScopeSafe =
            forcedReversalSet == 0 &&
            forcedReversalClear > 0;

        bool canTryRejectedFallbackUnsyncedShadow =
            fullShadowClean &&
            preferredInputClean &&
            fallbackNotFound == 0;

        return "Lady Bug EnemyWork sync-removal preflight " + Version + ": " +
               "updateOneSummaryFound=true" +
               ", providerMode=" + providerMode +
               ", exactProviderUsable=" + FormatBool(exactProviderUsable) +
               ", exactProviderTupleMismatches=" + FormatInt(exactTupleMismatches) +
               ", checks=" + FormatInt(checks) +
               ", matches=" + FormatInt(matches) +
               ", mismatches=" + FormatInt(mismatches) +
               ", preferredProviderChecks=" + FormatInt(preferredProviderChecks) +
               ", preferredProviderMatchesReference=" + FormatInt(preferredProviderMatchesReference) +
               ", preferredProviderDiffersFromReference=" + FormatInt(preferredProviderDiffersFromReference) +
               ", preferredProviderSkips=" + FormatInt(preferredProviderSkips) +
               ", fallbackSelected=" + FormatInt(fallbackSelected) +
               ", fallbackNotFound=" + FormatInt(fallbackNotFound) +
               ", preferredRejectedCurrentKept=" + FormatInt(preferredRejectedCurrentKept) +
               ", forcedReversalClear=" + FormatInt(forcedReversalClear) +
               ", forcedReversalSet=" + FormatInt(forcedReversalSet) +
               ", skippedMissingVram=" + FormatInt(skippedMissingVram) +
               ", tileAddressOutOfRange=" + FormatInt(tileAddressOutOfRange) +
               ", fullShadowClean=" + FormatBool(fullShadowClean) +
               ", preferredInputClean=" + FormatBool(preferredInputClean) +
               ", currentScopeSafe=" + FormatBool(currentScopeSafe) +
               ", canTryRejectedFallbackUnsyncedShadow=" + FormatBool(canTryRejectedFallbackUnsyncedShadow) +
               ". NOTE: preflight-only. The full Enemy_UpdateOne shadow comparison already includes modeled rejectedMask, 0x61C2/fallback helper, tempDir, tempX, and tempY. This says the next safe experiment is a shadow branch where SimulationState keeps modeled rejectedMask / 0x61C2 instead of immediately syncing them from MAME. The visible simulation still remains reference-assisted, and the forced-reversal carry-set path is not validated because forcedReversalSet=0 in this trace.";
    }

    private static int ExtractInt(string text, string key)
    {
        string value = ExtractValue(text, key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : -1;
    }

    private static bool ExtractBool(string text, string key)
    {
        string value = ExtractValue(text, key);
        return value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractValue(string text, string key)
    {
        int start = text.IndexOf(key, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        start += key.Length;
        int end = start;
        while (end < text.Length)
        {
            char c = text[end];
            if (c == ',' || c == ';' || c == '.')
                break;
            end++;
        }

        return text[start..end].Trim();
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string FormatInt(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
