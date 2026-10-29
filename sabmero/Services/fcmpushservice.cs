using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using sabmero.Data;

namespace sabmero.Services;

public interface IPushService
{
    // Sends a push notification to a single device token. Silently no-ops when
    // FCM isn't configured or the token is empty, so callers never need guards.
    Task SendToTokenAsync(string? fcmToken, string title, string body, Dictionary<string, string>? data = null);
}

// FCM HTTP v1 implementation.
//
// Setup:
//  1. Firebase Console → Project settings → Service accounts → Generate new private key.
//  2. Save the JSON file on the server (e.g. /app/secrets/firebase-service-account.json,
//     mounted via a Docker named volume — do NOT commit it to git).
//  3. appsettings.json:
//       "Fcm": { "ServiceAccountPath": "secrets/firebase-service-account.json" }
//     (ProjectId is read from the JSON itself.)
//
// This class builds the OAuth2 access token manually by signing a JWT with the
// service account's RSA key — no Google NuGet packages required on .NET 7.
public class FcmPushService : IPushService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<FcmPushService> _logger;
    private readonly AppDbContext _db;

    // Cached OAuth token (valid ~1 hour).
    private static string? _cachedToken;
    private static DateTime _cachedTokenExpiry = DateTime.MinValue;
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    public FcmPushService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<FcmPushService> logger, AppDbContext db)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
        _db = db;
    }

    public async Task SendToTokenAsync(string? fcmToken, string title, string body, Dictionary<string, string>? data = null)
    {
        if (string.IsNullOrWhiteSpace(fcmToken)) return;

        // Respect the "Push Notifications" switch on the Settings screen:
        // if the user who owns this device token turned it off, send nothing.
        try
        {
            if (await _db.Users.AsNoTracking().AnyAsync(u => u.FcmToken == fcmToken && !u.PushEnabled))
                return;
        }
        catch (Exception ex)
        {
            // If the check itself fails, fall through and send (fail-open).
            _logger.LogWarning(ex, "Could not check PushEnabled before sending push");
        }

        var saPath = _config["Fcm:ServiceAccountPath"];
        if (string.IsNullOrWhiteSpace(saPath) || !File.Exists(saPath))
        {
            _logger.LogWarning("[PUSH not configured] {Title}: {Body} → token {Token}", title, body, fcmToken[..Math.Min(12, fcmToken.Length)]);
            return;
        }

        try
        {
            var sa = JsonDocument.Parse(await File.ReadAllTextAsync(saPath)).RootElement;
            var projectId = sa.GetProperty("project_id").GetString()!;
            var accessToken = await GetAccessTokenAsync(sa);

            var message = new
            {
                message = new
                {
                    token = fcmToken,
                    notification = new { title, body },
                    data = data ?? new Dictionary<string, string>(),
                    android = new { priority = "HIGH", notification = new { channel_id = "sabmero_default" } },
                    apns = new { payload = new { aps = new { sound = "default", badge = 1 } } }
                }
            };

            var client = _httpFactory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send")
            {
                Content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                _logger.LogError("FCM send failed ({Status}): {Error}", resp.StatusCode, err);
            }
            else
            {
                _logger.LogInformation("Push sent: {Title}", title);
            }
        }
        catch (Exception ex)
        {
            // Push failures must never break the main request flow.
            _logger.LogError(ex, "FCM push error");
        }
    }

    // ── OAuth2 access token from the service account (JWT bearer grant) ─────
    private async Task<string> GetAccessTokenAsync(JsonElement sa)
    {
        if (_cachedToken != null && DateTime.UtcNow < _cachedTokenExpiry)
            return _cachedToken;

        await _tokenLock.WaitAsync();
        try
        {
            if (_cachedToken != null && DateTime.UtcNow < _cachedTokenExpiry)
                return _cachedToken;

            var clientEmail = sa.GetProperty("client_email").GetString()!;
            var privateKeyPem = sa.GetProperty("private_key").GetString()!;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = clientEmail,
                scope = "https://www.googleapis.com/auth/firebase.messaging",
                aud = "https://oauth2.googleapis.com/token",
                iat = now,
                exp = now + 3600
            }));

            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            var signature = Base64Url(rsa.SignData(
                Encoding.UTF8.GetBytes($"{header}.{claims}"),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            var jwt = $"{header}.{claims}.{signature}";

            var client = _httpFactory.CreateClient();
            var resp = await client.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = jwt
                }));
            resp.EnsureSuccessStatusCode();

            var tokenDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            _cachedToken = tokenDoc.GetProperty("access_token").GetString()!;
            var expiresIn = tokenDoc.GetProperty("expires_in").GetInt32();
            _cachedTokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 120); // refresh 2 min early
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}