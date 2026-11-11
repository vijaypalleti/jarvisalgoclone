using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Jarvis.Api.Services;

public record SavedFyersToken(string ClientId, string AccessToken, string? RefreshToken, DateTime SavedUtc);

/// <summary>
/// Keeps the token from the in-app Fyers login across API restarts.
/// File: %LOCALAPPDATA%\JarvisDemo\fyers-token.dat (outside the repo), encrypted with ASP.NET Data Protection
/// (keys protected by Windows DPAPI for your user), so it is unreadable if copied elsewhere.
/// </summary>
public class FyersTokenStore(IDataProtectionProvider dp, ILogger<FyersTokenStore> log)
{
    private readonly IDataProtector _protector = dp.CreateProtector("Jarvis.Fyers.Token.v1");
    public static string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JarvisDemo", "fyers-token.dat");

    public void Save(SavedFyersToken t)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, _protector.Protect(JsonSerializer.Serialize(t)));
    }

    public SavedFyersToken? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<SavedFyersToken>(_protector.Unprotect(File.ReadAllText(FilePath)));
        }
        catch (Exception ex)
        {
            log.LogWarning("Saved Fyers token unreadable ({Message}); log in again", ex.Message);
            return null;
        }
    }

    public void Delete() { try { File.Delete(FilePath); } catch { /* not there */ } }
}
