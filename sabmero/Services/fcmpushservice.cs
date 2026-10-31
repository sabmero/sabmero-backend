using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.Models;

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
    private readonly IServiceScopeFactory _scopeFactory;

    // Cached OAuth token (valid ~1 hour).
    private static string? _cachedToken;
    private static DateTime _cachedTokenExpiry = DateTime.MinValue;
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    public FcmPushService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<FcmPushService> logger, IServiceScopeFactory scopeFactory)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task SendToTokenAsync(string? fcmToken, string title, string body, Dictionary<string, string>? data = null)
    {
        if (string.IsNullOrWhiteSpace(fcmToken)) return;

        // 1) Save this notification to the user's Alerts-tab history (always, even
        //    if their Push switch is off).
        // 2) Respect the "Push Notifications" switch on the Settings screen: if the
        //    owner of this device token turned it off, send no push.
        // Uses its own DI scope so it never touches the caller's DbContext.
        var pushAllowed = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var owners = await db.Users.AsNoTracking()
                .Where(u => u.FcmToken == fcmToken)
                .Select(u => new { u.Id, u.PushEnabled })
                .ToListAsync();

            if (owners.Count > 0)
            {
                var type = "system";
                string? reference = null;
                if (data != null)
                {
                    if (data.TryGetValue("type", out var t) && !string.IsNullOrWhiteSpace(t)) type = t;
                    foreach (var key in new[] { "orderId", "bookingId", "paymentId", "requestId", "code" })
                    {
                        if (data.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) { reference = v; break; }
                    }
                }

                // A payment push carries bookingId for repair bookings → keep them apart
                // from order payments so the app opens the right screen.
                if (type == "payment" && data != null && data.ContainsKey("bookingId"))
                    type = "payment_booking";

                foreach (var o in owners)
                {
                    db.Notifications.Add(new AppNotification
                    {
                        UserId = o.Id,
                        Title = Trunc(title, 150),
                        Body = Trunc(body, 500),
                        Type = Trunc(type, 40),
                        ReferenceId = reference == null ? null : Trunc(reference, 50),
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                await db.SaveChangesAsync();

                pushAllowed = owners.Any(o => o.PushEnabled);
            }
        }
        catch (Exception ex)
        {
            // History/switch problems must never block the push itself.
            _logger.LogWarning(ex, "Could not save notification history / check PushEnabled");
        }
        if (!pushAllowed) return;

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

    private static string Trunc(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}