using Microsoft.Data.Sqlite;

namespace DeskNest.Core.Data;

/// <summary>SQLite 访问封装：建库、迁移、读写。所有访问串行化，避免多线程同时写。</summary>
public sealed class Db : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly object _gate = new();

    public Db(string? dbFile = null)
    {
        var path = dbFile ?? AppPaths.DbFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _conn = new SqliteConnection($"Data Source={path};Cache=Shared");
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA synchronous=NORMAL;");
        Migrate();
    }

    public string DbPath => _conn.DataSource;

    private void Migrate()
    {
        Exec("""
        CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);
        CREATE TABLE IF NOT EXISTS boxes(
          id TEXT PRIMARY KEY, name TEXT, kind INTEGER, storage_mode INTEGER, storage_root TEXT,
          group_id TEXT, mapped_path TEXT, view_mode TEXT, sort_mode TEXT, sort_descending INTEGER,
          icon_size INTEGER, text_size REAL, file_name_lines INTEGER, opacity REAL, material TEXT,
          material_intensity REAL, corner_radius INTEGER, style_scope INTEGER, lock_position INTEGER,
          lock_size INTEGER, rolled_up INTEGER, is_hidden INTEGER, auto_hide INTEGER, hide_file_name INTEGER,
          show_extensions INTEGER, show_hover_buttons INTEGER, monitor_key TEXT, anchor TEXT,
          margin_x REAL, margin_y REAL, width REAL, height REAL, collapsed_x REAL, collapsed_y REAL,
          created_at TEXT, updated_at TEXT);
        CREATE TABLE IF NOT EXISTS items(
          id TEXT PRIMARY KEY, box_id TEXT, kind INTEGER, display_name TEXT, source_path TEXT,
          stored_path TEXT, ext TEXT, size_bytes INTEGER, source_modified TEXT, added_at TEXT,
          is_favorite INTEGER, tags TEXT, order_index INTEGER, stack_key TEXT, is_missing INTEGER);
        CREATE INDEX IF NOT EXISTS ix_items_box ON items(box_id);
        CREATE TABLE IF NOT EXISTS box_groups(
          id TEXT PRIMARY KEY, name TEXT, member_ids TEXT, active_member_id TEXT, nav_style TEXT,
          wheel_switch INTEGER, hover_switch INTEGER, title_display TEXT, monitor_key TEXT,
          margin_x REAL, margin_y REAL, width REAL, height REAL, is_visible INTEGER);
        CREATE TABLE IF NOT EXISTS widgets(
          id TEXT PRIMARY KEY, kind INTEGER, name TEXT, group_id TEXT, box_id TEXT, monitor_key TEXT,
          x REAL, y REAL, width REAL, height REAL, opacity REAL, material TEXT, material_intensity REAL,
          corner_radius INTEGER, style_scope INTEGER, animation_effect TEXT, view_mode TEXT,
          is_visible INTEGER, is_collapsed INTEGER, config_json TEXT);
        CREATE TABLE IF NOT EXISTS todos(
          id TEXT PRIMARY KEY, title TEXT, note TEXT, list_name TEXT, priority INTEGER, repeat TEXT,
          due_at TEXT, remind_minutes_before INTEGER, is_done INTEGER, is_archived INTEGER,
          created_at TEXT, completed_at TEXT);
        CREATE TABLE IF NOT EXISTS notes(
          id TEXT PRIMARY KEY, title TEXT, body TEXT, format TEXT, is_pinned INTEGER, tags TEXT, updated_at TEXT);
        CREATE TABLE IF NOT EXISTS rules(
          id TEXT PRIMARY KEY, name TEXT, enabled INTEGER, priority INTEGER, conditions_json TEXT,
          actions_json TEXT, hit_count INTEGER);
        CREATE TABLE IF NOT EXISTS org_runs(
          id TEXT PRIMARY KEY, started_at TEXT, finished_at TEXT, trigger TEXT, moved_count INTEGER,
          undone INTEGER, moves_json TEXT);
        CREATE TABLE IF NOT EXISTS pets(
          id TEXT PRIMARY KEY, pack_id TEXT, name TEXT, scale REAL, opacity REAL, x REAL, y REAL,
          topmost_mode TEXT, keyboard_follow INTEGER, click_reaction INTEGER, system_reaction INTEGER,
          idle_reaction INTEGER, todo_reminder INTEGER, pomodoro_enabled INTEGER, ai_ask_enabled INTEGER,
          fps_limit INTEGER, is_visible INTEGER);
        CREATE TABLE IF NOT EXISTS usage_sessions(
          id TEXT PRIMARY KEY, process_name TEXT, window_title TEXT, start_at TEXT, end_at TEXT, duration_seconds INTEGER);
        CREATE TABLE IF NOT EXISTS power_plan(
          id TEXT PRIMARY KEY, enabled INTEGER, trigger INTEGER, clock_time TEXT, weekdays_only INTEGER,
          countdown_minutes INTEGER, idle_minutes INTEGER, warn_minutes_before INTEGER, action INTEGER,
          backup_before INTEGER, defer_on_activity INTEGER);
        CREATE TABLE IF NOT EXISTS api_providers(
          id TEXT PRIMARY KEY, name TEXT, base_url TEXT, model TEXT, key_cipher TEXT, manual_quota REAL,
          quota_warn_below REAL, enabled INTEGER);
        CREATE TABLE IF NOT EXISTS api_stats(
          id TEXT PRIMARY KEY, provider_id TEXT, at TEXT, success INTEGER, latency_ms INTEGER,
          purpose TEXT, error_kind TEXT);
        CREATE TABLE IF NOT EXISTS journal(
          id TEXT PRIMARY KEY, kind TEXT, payload_json TEXT, created_at TEXT, applied INTEGER);
        CREATE TABLE IF NOT EXISTS search_index(
          path TEXT PRIMARY KEY, is_dir INTEGER, size INTEGER, mtime INTEGER);
        """);
        if (QueryScalar("SELECT value FROM meta WHERE key='schema'") is null)
            Exec("INSERT INTO meta(key,value) VALUES('schema','1');");
    }

    public void Exec(string sql, params (string, object?)[] args)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public object? QueryScalar(string sql, params (string, object?)[] args)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            var r = cmd.ExecuteScalar();
            return r is DBNull ? null : r;
        }
    }

    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            var list = new List<T>();
            while (r.Read()) list.Add(map(r));
            return list;
        }
    }

    public long Count(string table) => Convert.ToInt64(QueryScalar($"SELECT COUNT(*) FROM {table}") ?? 0L);

    public void Dispose()
    {
        try { _conn.Close(); _conn.Dispose(); } catch { }
    }
}

public static class DbExtensions
{
    public static string? GetStringOrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    public static DateTimeOffset? GetDateOrNull(this SqliteDataReader r, int i)
        => r.IsDBNull(i) ? null : DateTimeOffset.Parse(r.GetString(i));
}
