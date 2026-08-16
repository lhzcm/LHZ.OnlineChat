using LHZ.FastJson;
using StackExchange.Redis;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// Redis 缓存服务
/// </summary>
public class RedisService : IDisposable
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;

    public RedisService(string connectionString)
    {
        _redis = ConnectionMultiplexer.Connect(connectionString);
        _db = _redis.GetDatabase();
        Console.WriteLine("✅ Redis connected successfully.");
    }

    public IDatabase Database => _db;

    // ==================== String 操作 ====================

    public async Task SetStringAsync(string key, string value, TimeSpan? expiry = null)
        => await _db.StringSetAsync(key, value, expiry.HasValue ? (StackExchange.Redis.Expiration)expiry.Value : StackExchange.Redis.Expiration.Default);

    public async Task<string?> GetStringAsync(string key)
        => await _db.StringGetAsync(key);

    public async Task SetJsonAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var json = JsonConvert.Serialize(value);
        await _db.StringSetAsync(key, json, expiry.HasValue ? (StackExchange.Redis.Expiration)expiry.Value : StackExchange.Redis.Expiration.Default);
    }

    public async Task<T?> GetJsonAsync<T>(string key) where T : class
    {
        var json = await _db.StringGetAsync(key);
        if (json.IsNullOrEmpty) return null;
        return JsonConvert.Deserialize<T>((string)json!);
    }

    public async Task<bool> KeyExistsAsync(string key)
        => await _db.KeyExistsAsync(key);

    public async Task DeleteKeyAsync(string key)
        => await _db.KeyDeleteAsync(key);

    // ==================== Set 操作 ====================

    public async Task SetAddAsync(string key, string value)
        => await _db.SetAddAsync(key, value);

    public async Task SetRemoveAsync(string key, string value)
        => await _db.SetRemoveAsync(key, value);

    public async Task<bool> SetContainsAsync(string key, string value)
        => await _db.SetContainsAsync(key, value);

    public async Task<string[]> SetMembersAsync(string key)
    {
        var members = await _db.SetMembersAsync(key);
        return members.Select(m => m.ToString()).ToArray();
    }

    // ==================== List 操作 ====================

    public async Task ListLeftPushAsync(string key, string value)
        => await _db.ListLeftPushAsync(key, value);

    public async Task ListTrimAsync(string key, long start, long stop)
        => await _db.ListTrimAsync(key, start, stop);

    public async Task<string[]> ListRangeAsync(string key, long start = 0, long stop = -1)
    {
        var items = await _db.ListRangeAsync(key, start, stop);
        return items.Select(i => i.ToString()).ToArray();
    }

    // ==================== Hash 操作 ====================

    public async Task HashSetAsync(string key, string field, string value)
        => await _db.HashSetAsync(key, field, value);

    public async Task<string?> HashGetAsync(string key, string field)
        => await _db.HashGetAsync(key, field);

    public async Task HashDeleteAsync(string key, string field)
        => await _db.HashDeleteAsync(key, field);

    public void Dispose()
    {
        _redis?.Dispose();
        GC.SuppressFinalize(this);
    }
}
