using System.Security.Cryptography;
using System.Text.Json;
using System.IO;

namespace DBLC;

/// <summary>Local window privacy preferences. This is a casual privacy lock, not data encryption.</summary>
public sealed class PrivacyLockStore
{
    private const int Iterations = 210_000;
    private readonly string _path;
    private PrivacySettings _settings;
    public event Action? Changed;

    public PrivacyLockStore() : this(AppDataPaths.File("privacy-lock.json")) { }

    public PrivacyLockStore(string path)
    {
        _path = path;
        try { _settings = JsonSerializer.Deserialize<PrivacySettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("The DBLC privacy settings are invalid."); }
        catch (FileNotFoundException) { _settings = new PrivacySettings(); }
        catch (DirectoryNotFoundException) { _settings = new PrivacySettings(); }
        if ((_settings.Salt is null) != (_settings.Verifier is null) ||
            (_settings.Salt is not null && (_settings.Salt.Length != 16 || _settings.Verifier!.Length != 32)))
            throw new InvalidDataException("The DBLC privacy settings are invalid.");
    }

    public bool CloseToTrayEnabled => _settings.CloseToTrayEnabled;
    public bool HasPin => _settings.Verifier is not null;

    public void SetCloseToTray(bool enabled) => Save(_settings with { CloseToTrayEnabled = enabled });

    public static bool IsFourDigitPin(string? pin) => pin is { Length: 4 } && pin.All(c => c is >= '0' and <= '9');

    public bool SetPin(string? pin, string? confirmation)
    {
        if (!IsFourDigitPin(pin) || !string.Equals(pin, confirmation, StringComparison.Ordinal)) return false;
        var salt = RandomNumberGenerator.GetBytes(16);
        var verifier = Rfc2898DeriveBytes.Pbkdf2(pin!, salt, Iterations, HashAlgorithmName.SHA256, 32);
        Save(_settings with { Salt = salt, Verifier = verifier });
        return true;
    }

    public bool Verify(string? pin)
    {
        if (!HasPin || !IsFourDigitPin(pin)) return false;
        var candidate = Rfc2898DeriveBytes.Pbkdf2(pin!, _settings.Salt!, Iterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(candidate, _settings.Verifier!);
    }

    public void RemovePin() => Save(_settings with { Salt = null, Verifier = null });
    public void ResetPin() => RemovePin();

    private void Save(PrivacySettings next)
    {
        AtomicTextFile.Write(_path, JsonSerializer.Serialize(next));
        _settings = next;
        Changed?.Invoke();
    }

    private sealed record PrivacySettings
    {
        public bool CloseToTrayEnabled { get; init; }
        public byte[]? Salt { get; init; }
        public byte[]? Verifier { get; init; }
    }
}

public enum DesktopCloseAction { Hide, Shutdown }

public static class DesktopClosePolicy
{
    public static DesktopCloseAction Decide(bool closeToTrayEnabled, bool explicitExit, bool systemEnding) =>
        closeToTrayEnabled && !explicitExit && !systemEnding ? DesktopCloseAction.Hide : DesktopCloseAction.Shutdown;
}
