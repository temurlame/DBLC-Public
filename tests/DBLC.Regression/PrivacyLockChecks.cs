using DBLC;
using System.IO;
using System.Text.Json;

internal static partial class Checks
{
    private static void PrivacyLockChecks(string temporary)
    {
        var path = Path.Combine(temporary, "privacy-checks", "privacy-lock.json");
        var privacy = new PrivacyLockStore(path);
        Require(!privacy.CloseToTrayEnabled && !privacy.HasPin, "privacy defaults leave tray and PIN off");
        var invalidPath = Path.Combine(temporary, "privacy-directory-instead-of-file");
        Directory.CreateDirectory(invalidPath);
        var refusedUnreadablePrivacy = false;
        try { _ = new PrivacyLockStore(invalidPath); }
        catch (IOException) { refusedUnreadablePrivacy = true; }
        catch (UnauthorizedAccessException) { refusedUnreadablePrivacy = true; }
        Require(refusedUnreadablePrivacy, "unreadable privacy settings fail closed instead of becoming defaults");
        Require(!PrivacyLockStore.IsFourDigitPin("123") && !PrivacyLockStore.IsFourDigitPin("12345") &&
            !PrivacyLockStore.IsFourDigitPin("12a4") && !PrivacyLockStore.IsFourDigitPin("１２３４") &&
            PrivacyLockStore.IsFourDigitPin("0042"), "PIN accepts exactly four ASCII decimal digits");
        Require(!privacy.SetPin("1234", "4321") && !privacy.HasPin, "mismatched confirmation cannot enable PIN");
        Require(!privacy.SetPin("12a4", "12a4") && !File.Exists(path), "invalid PIN does not persist privacy settings");
        privacy.SetCloseToTray(true);
        Require(privacy.SetPin("0042", "0042") && privacy.Verify("0042") && !privacy.Verify("0043"), "PIN verifier accepts only the configured PIN");
        var raw = File.ReadAllText(path);
        Require(!raw.Contains("0042", StringComparison.Ordinal) && !raw.Contains("Password", StringComparison.OrdinalIgnoreCase), "PIN is absent from saved settings");
        using (var json = JsonDocument.Parse(raw))
            Require(json.RootElement.GetProperty("Salt").GetBytesFromBase64().Length == 16 && json.RootElement.GetProperty("Verifier").GetBytesFromBase64().Length == 32,
                "privacy file contains salt and verifier");
        var reloaded = new PrivacyLockStore(path);
        Require(reloaded.CloseToTrayEnabled && reloaded.HasPin && reloaded.Verify("0042"), "atomic privacy file reload preserves preference and verifier");
        reloaded.SetCloseToTray(false);
        Require(reloaded.HasPin && reloaded.Verify("0042"), "disabling tray leaves startup PIN intact");
        reloaded.SetCloseToTray(true);
        reloaded.ResetPin();
        var reset = new PrivacyLockStore(path);
        Require(reset.CloseToTrayEnabled && !reset.HasPin && !reset.Verify("0042"), "local reset removes only PIN material");
        Require(!File.ReadAllText(path).Contains("0042", StringComparison.Ordinal), "reset file retains no PIN");
        reset.SetPin("1234", "1234");
        reset.RemovePin();
        Require(reset.CloseToTrayEnabled && !reset.HasPin, "removing PIN leaves tray preference intact");
        Require(DesktopClosePolicy.Decide(false, false, false) == DesktopCloseAction.Shutdown &&
            DesktopClosePolicy.Decide(true, false, false) == DesktopCloseAction.Hide &&
            DesktopClosePolicy.Decide(true, true, false) == DesktopCloseAction.Shutdown &&
            DesktopClosePolicy.Decide(true, false, true) == DesktopCloseAction.Shutdown,
            "X, tray Exit and Windows session ending choose the correct lifecycle paths");
    }
}
