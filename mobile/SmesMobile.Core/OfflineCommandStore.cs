using SQLite;

namespace SmesMobile.Core;

/// <summary>SQLite outbox. Authentication tokens and passwords are intentionally not represented here.</summary>
[Table("offline_commands")]
public sealed class OfflineCommandRecord
{
    [PrimaryKey, AutoIncrement]
    public int LocalId { get; set; }
    [Indexed]
    public string CommandId { get; set; } = string.Empty;
    [Indexed]
    public string OwnerKey { get; set; } = string.Empty;
    public long TenantId { get; set; }
    public long OwnerUserId { get; set; }
    [Indexed]
    public string Kind { get; set; } = string.Empty;
    public string Stage { get; set; } = OfflineCommandStages.Submit;
    public string State { get; set; } = OfflineCommandStates.Pending;
    public string PayloadJson { get; set; } = string.Empty;
    public long? TargetId { get; set; }
    public long? BillId { get; set; }
    public long? ServerJobId { get; set; }
    public string? ServerStatus { get; set; }
    public int RetryCount { get; set; }
    public int ServerAttempts { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Ignore]
    public bool IsUnknownNeedsReconciliation => State == OfflineCommandStates.UnknownNeedsReconciliation;
}

public sealed class OfflineCommandStore
{
    private static int _providerInitialized;
    private readonly SQLiteAsyncConnection _database;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public OfflineCommandStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (Interlocked.Exchange(ref _providerInitialized, 1) == 0)
            SQLitePCL.Batteries_V2.Init();
        _database = new SQLiteAsyncConnection(fullPath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        await _initializationLock.WaitAsync();
        try
        {
            if (_initialized) return;
            await _database.CreateTableAsync<OfflineCommandRecord>();
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<OfflineCommandRecord> EnqueueAsync(OfflineCommandRecord record)
    {
        await InitializeAsync();
        if (string.IsNullOrWhiteSpace(record.CommandId))
            record.CommandId = Guid.NewGuid().ToString("D");
        record.State = OfflineCommandStates.Pending;
        record.CreatedAt = DateTime.UtcNow;
        record.UpdatedAt = record.CreatedAt;
        await _database.InsertAsync(record);
        return record;
    }

    public async Task<List<OfflineCommandRecord>> ListAsync(string ownerKey)
    {
        await InitializeAsync();
        return await _database.Table<OfflineCommandRecord>()
            .Where(x => x.OwnerKey == ownerKey)
            .OrderByDescending(x => x.LocalId)
            .ToListAsync();
    }

    public async Task UpdateAsync(OfflineCommandRecord record)
    {
        await InitializeAsync();
        record.UpdatedAt = DateTime.UtcNow;
        await _database.UpdateAsync(record);
    }

    public async Task DeleteAsync(OfflineCommandRecord record)
    {
        await InitializeAsync();
        await _database.DeleteAsync(record);
    }
}
