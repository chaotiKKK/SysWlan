using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SysWlan.Core;

public sealed class Store : IDisposable
{
    private const int CurrentSchemaVersion = 2;
    private readonly object gate = new();
    private readonly SqliteConnection connection;
    private DateTimeOffset nextLogPrune;

    public Store(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        ExecuteBaseSchema();
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version < 1)
        {
            Execute("PRAGMA user_version=1;");
            version = 1;
        }
        if (version < 2) ApplyCalendarMigration();
    }

    private void ExecuteBaseSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS profiles (
              id TEXT PRIMARY KEY, name TEXT NOT NULL, notes TEXT NOT NULL DEFAULT '', gateway TEXT NOT NULL,
              mac TEXT NOT NULL, ssid TEXT NOT NULL, firstSeen TEXT NOT NULL, lastSeen TEXT NOT NULL,
              rx INTEGER NOT NULL DEFAULT 0, tx INTEGER NOT NULL DEFAULT 0, vendor TEXT NOT NULL, firmware TEXT);
            CREATE TABLE IF NOT EXISTS samples (
              id INTEGER PRIMARY KEY, profileId TEXT NOT NULL REFERENCES profiles(id), timestamp TEXT NOT NULL,
              rx REAL, tx REAL, latency INTEGER, signal INTEGER);
            CREATE INDEX IF NOT EXISTS idx_samples_profile_time ON samples(profileId,timestamp);
            CREATE TABLE IF NOT EXISTS logs (
              id INTEGER PRIMARY KEY, timestamp TEXT NOT NULL, source TEXT NOT NULL,
              severity TEXT NOT NULL, message TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS idx_logs_time ON logs(timestamp);
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """);
    }

    private void ApplyCalendarMigration()
    {
        using var transaction = connection.BeginTransaction();
        Execute(transaction, """
            CREATE TABLE IF NOT EXISTS device_profiles (
              id TEXT PRIMARY KEY, name TEXT NOT NULL, manualColor TEXT, icon TEXT,
              isVisible INTEGER NOT NULL DEFAULT 1, firstSeen TEXT NOT NULL, lastSeen TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS device_identity_members (
              sourceKey TEXT PRIMARY KEY, deviceProfileId TEXT NOT NULL REFERENCES device_profiles(id),
              mac TEXT, sourceKind TEXT NOT NULL, firstSeen TEXT NOT NULL, lastSeen TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS presence_intervals (
              id TEXT PRIMARY KEY, sourceKey TEXT NOT NULL REFERENCES device_identity_members(sourceKey),
              startedAt TEXT NOT NULL, endedAt TEXT, quality TEXT NOT NULL, isOpen INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS activity_aggregates (
              id TEXT PRIMARY KEY, sourceKey TEXT NOT NULL REFERENCES device_identity_members(sourceKey),
              startedAt TEXT NOT NULL, endedAt TEXT NOT NULL, receiveMbps REAL, sendMbps REAL,
              availability TEXT NOT NULL, source TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS device_events (
              id TEXT PRIMARY KEY, sourceKey TEXT NOT NULL REFERENCES device_identity_members(sourceKey),
              timestamp TEXT NOT NULL, type TEXT NOT NULL, severity TEXT NOT NULL,
              message TEXT NOT NULL, source TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS calendar_sync (
              objectType TEXT NOT NULL, objectId TEXT NOT NULL, uid TEXT NOT NULL,
              contentHash TEXT NOT NULL, etag TEXT, status TEXT NOT NULL,
              attempts INTEGER NOT NULL DEFAULT 0, nextAttemptAt TEXT, lastError TEXT, content TEXT NOT NULL DEFAULT '',
              PRIMARY KEY(objectType, objectId));
            CREATE INDEX IF NOT EXISTS idx_presence_time ON presence_intervals(startedAt, endedAt);
            CREATE INDEX IF NOT EXISTS idx_activity_time ON activity_aggregates(startedAt, endedAt);
            CREATE INDEX IF NOT EXISTS idx_device_events_time ON device_events(timestamp);
            """);
        try { Execute(transaction, "ALTER TABLE calendar_sync ADD COLUMN content TEXT NOT NULL DEFAULT '';"); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
        transaction.Commit();
        Execute("PRAGMA user_version=2;");
    }

    private static string Time(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string HashId(string prefix, string value) => prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    private static string DeviceProfileId(string sourceKey) => HashId("device-", sourceKey);
    private static string EventObjectId(string sourceKey, DateTimeOffset timestamp, DeviceEventType type) => HashId("event-", $"{sourceKey}|{type}|{Time(timestamp)}");

    private SqliteCommand Command(string sql, params (string Key, object? Value)[] parameters) => Command(sql, null, parameters);

    private SqliteCommand Command(string sql, SqliteTransaction? transaction, params (string Key, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return command;
    }

    private void Execute(string sql, params (string Key, object? Value)[] parameters)
    {
        using var command = Command(sql, null, parameters);
        command.ExecuteNonQuery();
    }

    private void Execute(SqliteTransaction transaction, string sql, params (string Key, object? Value)[] parameters)
    {
        using var command = Command(sql, transaction, parameters);
        command.ExecuteNonQuery();
    }

    public void Observe(NetworkSnapshot snapshot, TrafficSample traffic, RouterStatus router)
    {
        if (!snapshot.Connected) return;
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            var id = ProfileIdentity.Key(snapshot);
            Execute(transaction, """
                INSERT INTO profiles(id,name,gateway,mac,ssid,firstSeen,lastSeen,rx,tx,vendor,firmware)
                VALUES($id,$name,$gw,$mac,$ssid,$time,$time,$rx,$tx,$vendor,$firmware)
                ON CONFLICT(id) DO UPDATE SET gateway=$gw, mac=$mac, ssid=$ssid,lastSeen=$time,
                rx=profiles.rx+$rx,tx=profiles.tx+$tx,
                vendor=CASE WHEN $vendor='Unbekannt' THEN profiles.vendor ELSE $vendor END,
                firmware=COALESCE($firmware,profiles.firmware);
                """, ("$id", id), ("$name", snapshot.Ssid ?? snapshot.Gateway), ("$gw", snapshot.Gateway),
                ("$mac", snapshot.GatewayMac ?? ""), ("$ssid", snapshot.Ssid ?? ""), ("$time", Time(snapshot.Timestamp)),
                ("$rx", traffic.ReceivedDelta), ("$tx", traffic.SentDelta), ("$vendor", router.Vendor), ("$firmware", router.Firmware));
            Execute(transaction, "INSERT INTO samples(profileId,timestamp,rx,tx,latency,signal) VALUES($id,$time,$rx,$tx,$latency,$signal)",
                ("$id", id), ("$time", Time(snapshot.Timestamp)), ("$rx", traffic.ReceiveMbps), ("$tx", traffic.SendMbps),
                ("$latency", snapshot.GatewayLatencyMs), ("$signal", snapshot.Wlan.SignalPercent));
            transaction.Commit();
        }
    }

    public RouterProfile[] Profiles()
    {
        lock (gate)
        {
            using var command = Command("SELECT id,name,notes,gateway,mac,ssid,firstSeen,lastSeen,rx,tx,vendor,firmware FROM profiles ORDER BY lastSeen DESC");
            using var reader = command.ExecuteReader();
            var profiles = new List<RouterProfile>();
            while (reader.Read()) profiles.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), ParseTime(reader.GetString(6)), ParseTime(reader.GetString(7)), reader.GetInt64(8), reader.GetInt64(9), reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetString(11)));
            return profiles.ToArray();
        }
    }

    public void UpdateProfile(string id, string name, string notes)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || notes.Length > 2000) throw new ArgumentException("Name: 1–100 Zeichen, Notizen: maximal 2000 Zeichen.");
        lock (gate) Execute("UPDATE profiles SET name=$name,notes=$notes WHERE id=$id", ("$name", name.Trim()), ("$notes", notes), ("$id", id));
    }

    public TrafficSample[] History(string profileId, int count = 120)
    {
        lock (gate)
        {
            using var command = Command("SELECT timestamp,rx,tx FROM samples WHERE profileId=$id ORDER BY id DESC LIMIT $count", ("$id", profileId), ("$count", Math.Clamp(count, 1, 2000)));
            using var reader = command.ExecuteReader();
            var samples = new List<TrafficSample>();
            while (reader.Read()) samples.Add(new(ParseTime(reader.GetString(0)), reader.IsDBNull(1) ? null : reader.GetDouble(1), reader.IsDBNull(2) ? null : reader.GetDouble(2), 0, 0));
            samples.Reverse();
            return samples.ToArray();
        }
    }

    public void AddLog(LogEntry entry)
    {
        lock (gate)
        {
            Execute("INSERT INTO logs(timestamp,source,severity,message) VALUES($time,$source,$severity,$message)",
                ("$time", Time(entry.Timestamp)), ("$source", entry.Source), ("$severity", entry.Severity), ("$message", SyslogParser.Redact(entry.Message)));
            Execute("DELETE FROM logs WHERE id <= (SELECT MAX(id)-50000 FROM logs)");
            if (DateTimeOffset.UtcNow >= nextLogPrune)
            {
                var days = int.TryParse(GetSetting("retentionDays"), out var value) ? Math.Clamp(value, 1, 365) : 30;
                Execute("DELETE FROM logs WHERE timestamp < $cutoff", ("$cutoff", Time(DateTimeOffset.UtcNow.AddDays(-days))));
                nextLogPrune = DateTimeOffset.UtcNow.AddMinutes(1);
            }
        }
    }

    public LogEntry[] Logs(int count = 500)
    {
        lock (gate)
        {
            using var command = Command("SELECT timestamp,source,severity,message FROM logs ORDER BY id DESC LIMIT $count", ("$count", Math.Clamp(count, 1, 5000)));
            using var reader = command.ExecuteReader();
            var entries = new List<LogEntry>();
            while (reader.Read()) entries.Add(new(ParseTime(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            return entries.ToArray();
        }
    }

    public string? GetSetting(string key)
    {
        lock (gate)
        {
            using var command = Command("SELECT value FROM settings WHERE key=$key", ("$key", key));
            return command.ExecuteScalar() as string;
        }
    }

    public void SetSetting(string key, string value)
    {
        lock (gate) Execute("INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=$value", ("$key", key), ("$value", value));
    }

    public void Prune(int days)
    {
        if (days is < 1 or > 365) throw new ArgumentOutOfRangeException(nameof(days));
        lock (gate)
        {
            var cutoff = Time(DateTimeOffset.UtcNow.AddDays(-days));
            Execute("DELETE FROM samples WHERE timestamp<$cutoff; DELETE FROM logs WHERE timestamp<$cutoff; DELETE FROM logs WHERE id NOT IN (SELECT id FROM logs ORDER BY id DESC LIMIT 50000);", ("$cutoff", cutoff));
            if (SchemaVersion >= 2) PruneCalendarDataLocked(cutoff);
        }
    }

    private int SchemaVersion
    {
        get
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public CalendarDevice[] CalendarDevices()
    {
        lock (gate)
        {
            var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            using (var members = Command("SELECT deviceProfileId,sourceKey FROM device_identity_members ORDER BY sourceKey"))
            using (var reader = members.ExecuteReader())
                while (reader.Read())
                {
                    var id = reader.GetString(0);
                    if (!grouped.TryGetValue(id, out var sources)) grouped[id] = sources = [];
                    sources.Add(reader.GetString(1));
                }
            using var command = Command("SELECT id,name,manualColor,icon,isVisible,firstSeen,lastSeen FROM device_profiles ORDER BY name,id");
            using var profileReader = command.ExecuteReader();
            var result = new List<CalendarDevice>();
            while (profileReader.Read())
            {
                var id = profileReader.GetString(0);
                if (!grouped.TryGetValue(id, out var sources) || sources.Count == 0) continue;
                result.Add(new(id, profileReader.GetString(1), profileReader.IsDBNull(2) ? null : profileReader.GetString(2), profileReader.IsDBNull(3) ? null : profileReader.GetString(3), profileReader.GetInt64(4) != 0, sources.ToArray(), ParseTime(profileReader.GetString(5)), ParseTime(profileReader.GetString(6))));
            }
            return result.ToArray();
        }
    }

    public void SaveDeviceObservation(string sourceKey, string? mac, string name, DateTimeOffset observedAt, string sourceKind, PresenceInterval? presence, ActivityAggregate? activity, IEnumerable<DeviceEvent> events)
    {
        if (string.IsNullOrWhiteSpace(sourceKey)) throw new ArgumentException("Source key is required.", nameof(sourceKey));
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            var profileId = EnsureDeviceSource(transaction, sourceKey, mac, name, observedAt, sourceKind);
            if (presence is not null) SavePresenceLocked(transaction, presence);
            if (activity is not null) SaveActivityLocked(transaction, activity);
            SaveEventsLocked(transaction, events);
            transaction.Commit();
        }
    }

    public void SaveTimelineTransition(IEnumerable<PresenceInterval> intervals, IEnumerable<DeviceEvent> events)
    {
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            foreach (var interval in intervals) SavePresenceLocked(transaction, interval);
            SaveEventsLocked(transaction, events);
            transaction.Commit();
        }
    }

    private string EnsureDeviceSource(SqliteTransaction transaction, string sourceKey, string? mac, string name, DateTimeOffset observedAt, string sourceKind)
    {
        var existingCommand = Command("SELECT deviceProfileId FROM device_identity_members WHERE sourceKey=$sourceKey", transaction, ("$sourceKey", sourceKey));
        var existing = existingCommand.ExecuteScalar() as string;
        existingCommand.Dispose();
        var profileId = existing ?? DeviceProfileId(sourceKey);
        var displayName = string.IsNullOrWhiteSpace(name) ? "Unbekanntes Gerät" : name.Trim();
        Execute(transaction, """
            INSERT INTO device_profiles(id,name,manualColor,icon,isVisible,firstSeen,lastSeen)
            VALUES($id,$name,NULL,NULL,1,$time,$time)
            ON CONFLICT(id) DO UPDATE SET lastSeen=CASE WHEN device_profiles.lastSeen<$time THEN $time ELSE device_profiles.lastSeen END;
            """, ("$id", profileId), ("$name", displayName), ("$time", Time(observedAt)));
        Execute(transaction, """
            INSERT INTO device_identity_members(sourceKey,deviceProfileId,mac,sourceKind,firstSeen,lastSeen)
            VALUES($sourceKey,$profileId,$mac,$kind,$time,$time)
            ON CONFLICT(sourceKey) DO UPDATE SET lastSeen=CASE WHEN device_identity_members.lastSeen<$time THEN $time ELSE device_identity_members.lastSeen END,
              mac=COALESCE(device_identity_members.mac, excluded.mac);
            """, ("$sourceKey", sourceKey), ("$profileId", profileId), ("$mac", mac), ("$kind", sourceKind), ("$time", Time(observedAt)));
        return profileId;
    }

    private void SavePresenceLocked(SqliteTransaction transaction, PresenceInterval interval)
    {
        Execute(transaction, """
            INSERT INTO presence_intervals(id,sourceKey,startedAt,endedAt,quality,isOpen)
            VALUES($id,$source,$start,$end,$quality,$open)
            ON CONFLICT(id) DO UPDATE SET endedAt=$end,quality=$quality,isOpen=$open;
            """, ("$id", interval.Id), ("$source", interval.SourceKey), ("$start", Time(interval.Start)),
            ("$end", interval.End is null ? null : Time(interval.End.Value)), ("$quality", interval.Quality), ("$open", interval.IsOpen ? 1 : 0));
    }

    private void SaveActivityLocked(SqliteTransaction transaction, ActivityAggregate activity)
    {
        Execute(transaction, """
            INSERT INTO activity_aggregates(id,sourceKey,startedAt,endedAt,receiveMbps,sendMbps,availability,source)
            VALUES($id,$source,$start,$end,$rx,$tx,$availability,$origin)
            ON CONFLICT(id) DO UPDATE SET endedAt=$end,receiveMbps=$rx,sendMbps=$tx,availability=$availability,source=$origin;
            """, ("$id", activity.Id), ("$source", activity.SourceKey), ("$start", Time(activity.Start)), ("$end", Time(activity.End)),
            ("$rx", activity.ReceiveMbps), ("$tx", activity.SendMbps), ("$availability", activity.Availability.ToString()), ("$origin", activity.Source));
    }

    private void SaveEventsLocked(SqliteTransaction transaction, IEnumerable<DeviceEvent> events)
    {
        foreach (var entry in events)
            Execute(transaction, "INSERT INTO device_events(id,sourceKey,timestamp,type,severity,message,source) VALUES($id,$source,$time,$type,$severity,$message,$origin) ON CONFLICT(id) DO NOTHING",
                ("$id", entry.Id), ("$source", entry.SourceKey), ("$time", Time(entry.Timestamp)), ("$type", entry.Type.ToString()), ("$severity", entry.Severity), ("$message", SyslogParser.Redact(entry.Message)), ("$origin", entry.Source));
    }

    public void UpdateCalendarDevice(string id, string name, string? manualColor, string? icon, bool isVisible)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) throw new ArgumentException("Gerätename: 1–100 Zeichen.");
        if (manualColor is not null && !DeviceIdentity.IsValidColor(manualColor)) throw new ArgumentException("Farbe muss als #RRGGBB angegeben werden.");
        lock (gate) Execute("UPDATE device_profiles SET name=$name,manualColor=$color,icon=$icon,isVisible=$visible WHERE id=$id", ("$name", name.Trim()), ("$color", manualColor?.ToUpperInvariant()), ("$icon", icon), ("$visible", isVisible ? 1 : 0), ("$id", id));
    }

    public string MergeDeviceProfiles(string targetId, IEnumerable<string> sourceIds)
    {
        var ids = sourceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Where(id => id != targetId).ToArray();
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            foreach (var sourceId in ids) Execute(transaction, "UPDATE device_identity_members SET deviceProfileId=$target WHERE deviceProfileId=$source", ("$target", targetId), ("$source", sourceId));
            transaction.Commit();
        }
        return targetId;
    }

    public string SplitDeviceSources(string profileId, IEnumerable<string> sourceKeys, string newName, string? manualColor)
    {
        var sources = sourceKeys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal).ToArray();
        if (sources.Length == 0) throw new ArgumentException("Mindestens eine Gerätequelle auswählen.");
        if (string.IsNullOrWhiteSpace(newName) || newName.Length > 100) throw new ArgumentException("Gerätename: 1–100 Zeichen.");
        if (manualColor is not null && !DeviceIdentity.IsValidColor(manualColor)) throw new ArgumentException("Farbe muss als #RRGGBB angegeben werden.");
        var newId = "device-" + Guid.NewGuid().ToString("N");
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            var now = Time(DateTimeOffset.UtcNow);
            Execute(transaction, "INSERT INTO device_profiles(id,name,manualColor,icon,isVisible,firstSeen,lastSeen) VALUES($id,$name,$color,NULL,1,$time,$time)", ("$id", newId), ("$name", newName.Trim()), ("$color", manualColor?.ToUpperInvariant()), ("$time", now));
            foreach (var source in sources) Execute(transaction, "UPDATE device_identity_members SET deviceProfileId=$new WHERE sourceKey=$source AND deviceProfileId=$old", ("$new", newId), ("$source", source), ("$old", profileId));
            transaction.Commit();
        }
        return newId;
    }

    public CalendarSnapshot QueryCalendar(DateTimeOffset fromUtc, DateTimeOffset toUtc, bool includeOpen = true)
    {
        fromUtc = fromUtc.ToUniversalTime();
        toUtc = toUtc.ToUniversalTime();
        if (toUtc <= fromUtc) throw new ArgumentException("Der Kalenderzeitraum muss positiv sein.");
        lock (gate)
        {
            var presence = new List<PresenceInterval>();
            var presenceSql = includeOpen
                ? "SELECT id,sourceKey,startedAt,endedAt,quality,isOpen FROM presence_intervals WHERE startedAt<$to AND (endedAt IS NULL OR endedAt>$from) ORDER BY startedAt,id"
                : "SELECT id,sourceKey,startedAt,endedAt,quality,isOpen FROM presence_intervals WHERE endedAt IS NOT NULL AND startedAt<$to AND endedAt>$from ORDER BY startedAt,id";
            using (var command = Command(presenceSql, ("$from", Time(fromUtc)), ("$to", Time(toUtc))))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) presence.Add(new(reader.GetString(0), reader.GetString(1), ParseTime(reader.GetString(2)), reader.IsDBNull(3) ? null : ParseTime(reader.GetString(3)), reader.GetString(4), reader.GetInt64(5) != 0));

            var activity = new List<ActivityAggregate>();
            using (var command = Command("SELECT id,sourceKey,startedAt,endedAt,receiveMbps,sendMbps,availability,source FROM activity_aggregates WHERE startedAt<$to AND endedAt>$from ORDER BY startedAt,id", ("$from", Time(fromUtc)), ("$to", Time(toUtc))))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) activity.Add(new(reader.GetString(0), reader.GetString(1), ParseTime(reader.GetString(2)), ParseTime(reader.GetString(3)), reader.IsDBNull(4) ? null : reader.GetDouble(4), reader.IsDBNull(5) ? null : reader.GetDouble(5), Enum.TryParse<ActivityAvailability>(reader.GetString(6), out var availability) ? availability : ActivityAvailability.Unavailable, reader.GetString(7)));

            var events = new List<DeviceEvent>();
            using (var command = Command("SELECT id,sourceKey,timestamp,type,severity,message,source FROM device_events WHERE timestamp >= $from AND timestamp < $to ORDER BY timestamp,id", ("$from", Time(fromUtc)), ("$to", Time(toUtc))))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) events.Add(new(reader.GetString(0), reader.GetString(1), ParseTime(reader.GetString(2)), Enum.TryParse<DeviceEventType>(reader.GetString(3), out var type) ? type : DeviceEventType.AdapterEvent, reader.GetString(4), reader.GetString(5), reader.GetString(6)));

            return new(CalendarDevices(), presence.ToArray(), activity.ToArray(), events.ToArray());
        }
    }

    public PresenceInterval[] OpenPresence()
    {
        lock (gate)
        {
            using var command = Command("SELECT id,sourceKey,startedAt,endedAt,quality,isOpen FROM presence_intervals WHERE isOpen=1");
            using var reader = command.ExecuteReader();
            var result = new List<PresenceInterval>();
            while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), ParseTime(reader.GetString(2)), reader.IsDBNull(3) ? null : ParseTime(reader.GetString(3)), reader.GetString(4), true));
            return result.ToArray();
        }
    }

    public void QueueCalendarExport(IICalendarExporter exporter, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var snapshot = QueryCalendar(fromUtc, toUtc, true);
        var content = exporter.Export(snapshot);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        const string objectType = "calendar";
        const string objectId = "networkdevices";
        const string uid = "networkdevices@syswlaninfo.local";
        lock (gate)
        {
            var existing = SyncRecord(objectType, objectId);
            if (existing is not null && existing.ContentHash == hash && existing.Status == SyncStatus.Synced) return;
            var attempts = existing is null || existing.ContentHash != hash ? 0 : existing.Attempts;
            SaveSyncRecord(new CalendarSyncRecord(objectType, objectId, uid, hash, existing?.ETag, SyncStatus.Pending, attempts, null, null, content));
        }
    }

    public CalendarSyncRecord[] PendingSync(DateTimeOffset nowUtc, int limit = 100)
    {
        lock (gate)
        {
            using var command = Command("SELECT objectType,objectId,uid,contentHash,etag,status,attempts,nextAttemptAt,lastError,content FROM calendar_sync WHERE status<>$synced AND (nextAttemptAt IS NULL OR nextAttemptAt<=$now) ORDER BY COALESCE(nextAttemptAt,'') LIMIT $limit", ("$synced", SyncStatus.Synced.ToString()), ("$now", Time(nowUtc)), ("$limit", Math.Clamp(limit, 1, 1000)));
            using var reader = command.ExecuteReader();
            var result = new List<CalendarSyncRecord>();
            while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), Enum.TryParse<SyncStatus>(reader.GetString(5), out var status) ? status : SyncStatus.Pending, reader.GetInt32(6), reader.IsDBNull(7) ? null : ParseTime(reader.GetString(7)), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9)));
            return result.ToArray();
        }
    }

    public void SaveSyncRecord(CalendarSyncRecord record)
    {
        lock (gate) Execute("INSERT INTO calendar_sync(objectType,objectId,uid,contentHash,etag,status,attempts,nextAttemptAt,lastError,content) VALUES($type,$id,$uid,$hash,$etag,$status,$attempts,$next,$error,$content) ON CONFLICT(objectType,objectId) DO UPDATE SET uid=$uid,contentHash=$hash,etag=$etag,status=$status,attempts=$attempts,nextAttemptAt=$next,lastError=$error,content=$content", ("$type", record.ObjectType), ("$id", record.ObjectId), ("$uid", record.Uid), ("$hash", record.ContentHash), ("$etag", record.ETag), ("$status", record.Status.ToString()), ("$attempts", record.Attempts), ("$next", record.NextAttemptAt is null ? null : Time(record.NextAttemptAt.Value)), ("$error", record.LastError), ("$content", record.Content));
    }

    public CalendarSyncRecord? SyncRecord(string objectType, string objectId)
    {
        lock (gate)
        {
            using var command = Command("SELECT objectType,objectId,uid,contentHash,etag,status,attempts,nextAttemptAt,lastError,content FROM calendar_sync WHERE objectType=$type AND objectId=$id", ("$type", objectType), ("$id", objectId));
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), Enum.TryParse<SyncStatus>(reader.GetString(5), out var status) ? status : SyncStatus.Pending, reader.GetInt32(6), reader.IsDBNull(7) ? null : ParseTime(reader.GetString(7)), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9));
        }
    }

    public void PruneCalendarData(int days)
    {
        if (days is < 1 or > 365) throw new ArgumentOutOfRangeException(nameof(days));
        lock (gate) PruneCalendarDataLocked(Time(DateTimeOffset.UtcNow.AddDays(-days)));
    }

    private void PruneCalendarDataLocked(string cutoff)
    {
        Execute("DELETE FROM activity_aggregates WHERE endedAt<$cutoff; DELETE FROM device_events WHERE timestamp<$cutoff AND NOT EXISTS (SELECT 1 FROM calendar_sync s WHERE s.objectType='event' AND s.objectId=device_events.id); DELETE FROM presence_intervals WHERE endedAt<$cutoff AND NOT EXISTS (SELECT 1 FROM calendar_sync s WHERE s.objectType='presence' AND s.objectId=presence_intervals.id);", ("$cutoff", cutoff));
    }

    public string Export() => JsonSerializer.Serialize(new { schemaVersion = CurrentSchemaVersion, exportedAt = DateTimeOffset.UtcNow, profiles = Profiles(), logs = Logs(1000), history = Profiles().ToDictionary(p => p.Id, p => History(p.Id, 2000)), devices = CalendarDevices() }, new JsonSerializerOptions { WriteIndented = true });
    public void Dispose() { lock (gate) connection.Dispose(); }
}
