using System;
using System.Globalization;

/// <summary>
/// v0.7.16 non-invasive shadow-readiness check for removing the MAME sync bridge
/// around EnemyWork.rejectedMask / 0x61C1 and fallback helper / 0x61C2.
///
/// This deliberately does not change the visible comparison timeline yet.  It uses
/// the already computed full Enemy_UpdateOne shadow summary as the source of truth:
/// that shadow compares modeled rejectedMask, modeled 0x61C2, modeled tempDir,
/// modeled tempX, and modeled tempY against MAME after each active update.
///
/// If that full shadow is clean and the v0.7.15 preflight is clean, the next patch
/// can safely try a real runtime no-sync experiment where SimulationState does not
/// immediately overwrite rejectedMask / 0x61C2 from MAME.
/// </summary>
public static class LadyBugEnemyWorkRejectedFallbackUnsyncedShadowModel
{
    private const string Version = "v0.7.16";

    public static string BuildSummary(
        string enemyUpdateOneShadowSummary,
        string syncRemovalPreflightSummary)
    {
        if (string.IsNullOrWhiteSpace(enemyUpdateOneShadowSummary))
        {
            return "Lady Bug EnemyWork rejected/fallback unsynced shadow " + Version +
                   ": updateOneSummaryFound=false, rejectedFallbackUnsyncedShadowClean=false" +
                   ". NOTE: no Enemy_UpdateOne shadow summary was available.";
        }

        int checks = ExtractInt(enemyUpdateOneShadowSummary, "checks=");
        int matches = ExtractInt(enemyUpdateOneShadowSummary, "matches=");
        int mismatches = ExtractInt(enemyUpdateOneShadowSummary, "mismatches=");
        int preferredAccepted = ExtractInt(enemyUpdateOneShadowSummary, "preferredAccepted=");
        int preferredRejectedCurrentKept = ExtractInt(enemyUpdateOneShadowSummary, "preferredRejectedCurrentKept=");
        int fallbackSelected = ExtractInt(enemyUpdateOneShadowSummary, "fallbackSelected=");
        int fallbackNotFound = ExtractInt(enemyUpdateOneShadowSummary, "fallbackNotFound=");
        int forcedReversalClear = ExtractInt(enemyUpdateOneShadowSummary, "forcedReversalClear=");
        int forcedReversalSet = ExtractInt(enemyUpdateOneShadowSummary, "forcedReversalSet=");
        int preferredProviderChecks = ExtractInt(enemyUpdateOneShadowSummary, "preferredProviderChecks=");
        int preferredProviderDiffersFromReference = ExtractInt(enemyUpdateOneShadowSummary, "preferredProviderDiffersFromReference=");
        int preferredProviderSkips = ExtractInt(enemyUpdateOneShadowSummary, "preferredProviderSkips=");
        int skippedMissingVram = ExtractInt(enemyUpdateOneShadowSummary, "skippedMissingVram=");
        int tileAddressOutOfRange = ExtractInt(enemyUpdateOneShadowSummary, "tileAddressOutOfRange=");

        string preferredProviderMode = ExtractValue(enemyUpdateOneShadowSummary, "preferredProviderMode=");
        bool exactProviderUsable = ExtractBool(enemyUpdateOneShadowSummary, "preferredExactProviderUsable=");
        int exactTupleMismatches = ExtractInt(enemyUpdateOneShadowSummary, "preferredExactProviderBestWindowTupleMismatches=");

        bool preflightFound = !string.IsNullOrWhiteSpace(syncRemovalPreflightSummary);
        bool preflightAllows = ExtractBool(syncRemovalPreflightSummary, "canTryRejectedFallbackUnsyncedShadow=");

        bool fullScratchClean =
            checks > 0 &&
            matches == checks &&
            mismatches == 0 &&
            skippedMissingVram == 0 &&
            tileAddressOutOfRange == 0;

        bool exactPreferredClean =
            preferredProviderMode == "exact-PC-aligned" &&
            exactProviderUsable &&
            exactTupleMismatches == 0 &&
            preferredProviderChecks == checks &&
            preferredProviderDiffersFromReference == 0 &&
            preferredProviderSkips == 0;

        bool rejectedFallbackCovered =
            preferredRejectedCurrentKept > 0 &&
            fallbackSelected > 0 &&
            fallbackNotFound == 0;

        bool scopeKnown =
            forcedReversalSet == 0 &&
            forcedReversalClear > 0;

        bool rejectedFallbackUnsyncedShadowClean =
            fullScratchClean &&
            exactPreferredClean &&
            rejectedFallbackCovered &&
            preflightAllows;

        bool canTryRuntimeNoSyncPatch =
            rejectedFallbackUnsyncedShadowClean &&
            scopeKnown;

        return "Lady Bug EnemyWork rejected/fallback unsynced shadow " + Version + ": " +
               "updateOneSummaryFound=true" +
               ", preflightFound=" + FormatBool(preflightFound) +
               ", preflightAllows=" + FormatBool(preflightAllows) +
               ", preferredProviderMode=" + preferredProviderMode +
               ", exactProviderUsable=" + FormatBool(exactProviderUsable) +
               ", exactProviderTupleMismatches=" + FormatInt(exactTupleMismatches) +
               ", modeledScratchChecks=" + FormatInt(checks) +
               ", modeledScratchMatches=" + FormatInt(matches) +
               ", modeledScratchMismatches=" + FormatInt(mismatches) +
               ", preferredProviderChecks=" + FormatInt(preferredProviderChecks) +
               ", preferredProviderDiffersFromReference=" + FormatInt(preferredProviderDiffersFromReference) +
               ", preferredProviderSkips=" + FormatInt(preferredProviderSkips) +
               ", preferredAccepted=" + FormatInt(preferredAccepted) +
               ", preferredRejectedCurrentKept=" + FormatInt(preferredRejectedCurrentKept) +
               ", fallbackSelected=" + FormatInt(fallbackSelected) +
               ", fallbackNotFound=" + FormatInt(fallbackNotFound) +
               ", forcedReversalClear=" + FormatInt(forcedReversalClear) +
               ", forcedReversalSet=" + FormatInt(forcedReversalSet) +
               ", fullScratchClean=" + FormatBool(fullScratchClean) +
               ", exactPreferredClean=" + FormatBool(exactPreferredClean) +
               ", rejectedFallbackCovered=" + FormatBool(rejectedFallbackCovered) +
               ", rejectedFallbackUnsyncedShadowClean=" + FormatBool(rejectedFallbackUnsyncedShadowClean) +
               ", canTryRuntimeNoSyncPatch=" + FormatBool(canTryRuntimeNoSyncPatch) +
               ". NOTE: shadow-readiness only. This confirms that the full source-first Enemy_UpdateOne shadow already produces the same rejectedMask / 0x61C1 and fallback helper / 0x61C2 outcomes for the current 496 active updates. It does not yet remove the reference-sync writes in SimulationState. The next patch can try that runtime no-sync change, still behind validation, while remembering that forcedReversalSet=0 leaves the 0x4347 branch unvalidated.";
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
        if (string.IsNullOrEmpty(text))
            return string.Empty;

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
