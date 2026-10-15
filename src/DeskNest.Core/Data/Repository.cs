using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace DeskNest.Core.Data;

public sealed class Repository
{
    private readonly Db _db;
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    public Repository(Db db) => _db = db;

    // ---------- Boxes ----------
    public void Upsert(BoxRecord b)
    {
        b.UpdatedAt = DateTimeOffset.Now;
        _db.Exec("""
        INSERT INTO boxes VALUES($id,$name,$kind,$sm,$sr,$gid,$mp,$vm,$sort,$sd,$is,$ts,$fnl,$op,$mat,$mi,$cr,$ss,
          $lp,$ls,$ru,$ih,$ah,$hfn,$se,$shb,$mk,$anchor,$mx,$my,$w,$h,$cx,$cy,$ca,$ua)
        ON CONFLICT(id) DO UPDATE SET name=$name,kind=$kind,storage_mode=$sm,storage_root=$sr,group_id=$gid,
          mapped_path=$mp,view_mode=$vm,sort_mode=$sort,sort_descending=$sd,icon_size=$is,text_size=$ts,
          file_name_lines=$fnl,opacity=$op,material=$mat,material_intensity=$mi,corner_radius=$cr,style_scope=$ss,
          lock_position=$lp,lock_size=$ls,rolled_up=$ru,is_hidden=$ih,auto_hide=$ah,hide_file_name=$hfn,
          show_extensions=$se,show_hover_buttons=$shb,monitor_key=$mk,anchor=$anchor,margin_x=$mx,margin_y=$my,
          width=$w,height=$h,collapsed_x=$cx,collapsed_y=$cy,updated_at=$ua;
        """,
        ("$id", b.Id), ("$name", b.Name), ("$kind", (int)b.Kind), ("$sm", (int)b.StorageMode),
        ("$sr", b.StorageRoot), ("$gid", b.GroupId), ("$mp", b.MappedPath), ("$vm", b.ViewMode),
        ("$sort", b.SortMode), ("$sd", b.SortDescending ? 1 : 0), ("$is", b.IconSize), ("$ts", b.TextSize),
        ("$fnl", b.FileNameLines), ("$op", b.Opacity), ("$mat", b.Material), ("$mi", b.MaterialIntensity),
        ("$cr", b.CornerRadius), ("$ss", (int)b.StyleScope), ("$lp", b.LockPosition ? 1 : 0),
        ("$ls", b.LockSize ? 1 : 0), ("$ru", b.RolledUp ? 1 : 0), ("$ih", b.IsHidden ? 1 : 0),
        ("$ah", b.AutoHide ? 1 : 0), ("$hfn", b.HideFileName ? 1 : 0), ("$se", b.ShowExtensions ? 1 : 0),
        ("$shb", b.ShowHoverButtons ? 1 : 0), ("$mk", b.MonitorKey), ("$anchor", b.Anchor),
        ("$mx", b.MarginX), ("$my", b.MarginY), ("$w", b.Width), ("$h", b.Height),
        ("$cx", b.CollapsedX), ("$cy", b.CollapsedY),
        ("$ca", b.CreatedAt.ToString("o")), ("$ua", b.UpdatedAt.ToString("o")));
    }

    public List<BoxRecord> Boxes() => _db.Query("SELECT * FROM boxes ORDER BY created_at", MapBox);

    public BoxRecord? Box(string id) => _db.Query("SELECT * FROM boxes WHERE id=$id", MapBox, ("$id", id)).FirstOrDefault();

    public void DeleteBox(string id)
    {
        _db.Exec("DELETE FROM items WHERE box_id=$id; DELETE FROM boxes WHERE id=$id;", ("$id", id));
    }

    private static BoxRecord MapBox(SqliteDataReader r) => new()
    {
        Id = r.GetString(0), Name = r.GetString(1), Kind = (BoxKind)r.GetInt32(2),
        StorageMode = (StorageMode)r.GetInt32(3), StorageRoot = r.GetStringOrNull(4), GroupId = r.GetStringOrNull(5),
        MappedPath = r.GetStringOrNull(6), ViewMode = r.GetString(7), SortMode = r.GetString(8),
        SortDescending = r.GetInt32(9) != 0, IconSize = r.GetInt32(10), TextSize = r.GetDouble(11),
        FileNameLines = r.GetInt32(12), Opacity = r.GetDouble(13), Material = r.GetString(14),
        MaterialIntensity = r.GetDouble(15), CornerRadius = r.GetInt32(16), StyleScope = (StyleScope)r.GetInt32(17),
        LockPosition = r.GetInt32(18) != 0, LockSize = r.GetInt32(19) != 0, RolledUp = r.GetInt32(20) != 0,
        IsHidden = r.GetInt32(21) != 0, AutoHide = r.GetInt32(22) != 0, HideFileName = r.GetInt32(23) != 0,
        ShowExtensions = r.GetInt32(24) != 0, ShowHoverButtons = r.GetInt32(25) != 0, MonitorKey = r.GetString(26),
        Anchor = r.GetString(27), MarginX = r.GetDouble(28), MarginY = r.GetDouble(29),
        Width = r.GetDouble(30), Height = r.GetDouble(31), CollapsedX = r.GetDouble(32), CollapsedY = r.GetDouble(33),
        CreatedAt = DateTimeOffset.Parse(r.GetString(34)), UpdatedAt = DateTimeOffset.Parse(r.GetString(35))
    };

    // ---------- Items ----------
    public void Upsert(ItemRecord i) => _db.Exec("""
      INSERT INTO items VALUES($id,$b,$k,$dn,$sp,$st,$e,$sz,$sm,$at,$f,$tg,$oi,$sk,$im)
      ON CONFLICT(id) DO UPDATE SET box_id=$b,kind=$k,display_name=$dn,source_path=$sp,stored_path=$st,ext=$e,
        size_bytes=$sz,source_modified=$sm,added_at=$at,is_favorite=$f,tags=$tg,order_index=$oi,
        stack_key=$sk,is_missing=$im;
      """,
      ("$id", i.Id), ("$b", i.BoxId), ("$k", (int)i.Kind), ("$dn", i.DisplayName), ("$sp", i.SourcePath),
      ("$st", i.StoredPath), ("$e", i.Ext), ("$sz", i.SizeBytes),
      ("$sm", i.SourceModified?.ToString("o")), ("$at", i.AddedAt.ToString("o")),
      ("$f", i.IsFavorite ? 1 : 0), ("$tg", i.Tags), ("$oi", i.OrderIndex), ("$sk", i.StackKey),
      ("$im", i.IsMissing ? 1 : 0));

    public List<ItemRecord> Items(string boxId, bool mapped)
    {
        var sql = mapped
            ? "SELECT * FROM items WHERE box_id=$b ORDER BY is_favorite DESC, order_index, display_name"
            : "SELECT * FROM items WHERE box_id=$b ORDER BY is_favorite DESC, order_index, display_name";
        return _db.Query(sql, MapItem, ("$b", boxId));
    }

    public List<ItemRecord> AllItems() => _db.Query("SELECT * FROM items", MapItem);
    public void DeleteItem(string id) => _db.Exec("DELETE FROM items WHERE id=$id", ("$id", id));

    private static ItemRecord MapItem(SqliteDataReader r) => new()
    {
        Id = r.GetString(0), BoxId = r.GetString(1), Kind = (ItemKind)r.GetInt32(2), DisplayName = r.GetString(3),
        SourcePath = r.GetString(4), StoredPath = r.GetStringOrNull(5), Ext = r.GetString(6),
        SizeBytes = r.GetInt64(7), SourceModified = r.GetDateOrNull(8), AddedAt = DateTimeOffset.Parse(r.GetString(9)),
        IsFavorite = r.GetInt32(10) != 0, Tags = r.GetStringOrNull(11), OrderIndex = r.GetInt32(12),
        StackKey = r.GetStringOrNull(13), IsMissing = r.GetInt32(14) != 0
    };

    // ---------- Box groups ----------
    public void Upsert(BoxGroupRecord g) => _db.Exec("""
      INSERT INTO box_groups VALUES($id,$n,$m,$a,$ns,$w,$h,$t,$mk,$mx,$my,$gw,$gh,$v)
      ON CONFLICT(id) DO UPDATE SET name=$n,member_ids=$m,active_member_id=$a,nav_style=$ns,wheel_switch=$w,
        hover_switch=$h,title_display=$t,monitor_key=$mk,margin_x=$mx,margin_y=$my,width=$gw,height=$gh,is_visible=$v;
      """,
      ("$id", g.Id), ("$n", g.Name), ("$m", g.MemberIds), ("$a", g.ActiveMemberId), ("$ns", g.NavStyle),
      ("$w", g.WheelSwitch ? 1 : 0), ("$h", g.HoverSwitch ? 1 : 0), ("$t", g.TitleDisplay),
      ("$mk", g.MonitorKey), ("$mx", g.MarginX), ("$my", g.MarginY), ("$gw", g.Width), ("$gh", g.Height),
      ("$v", g.IsVisible ? 1 : 0));

    public List<BoxGroupRecord> Groups() => _db.Query("SELECT * FROM box_groups", r => new BoxGroupRecord
    {
        Id = r.GetString(0), Name = r.GetString(1), MemberIds = r.GetString(2), ActiveMemberId = r.GetStringOrNull(3),
        NavStyle = r.GetString(4), WheelSwitch = r.GetInt32(5) != 0, HoverSwitch = r.GetInt32(6) != 0,
        TitleDisplay = r.GetString(7), MonitorKey = r.GetString(8), MarginX = r.GetDouble(9),
        MarginY = r.GetDouble(10), Width = r.GetDouble(11), Height = r.GetDouble(12), IsVisible = r.GetInt32(13) != 0
    });

    public void DeleteGroup(string id) => _db.Exec("DELETE FROM box_groups WHERE id=$id", ("$id", id));

    // ---------- Widgets ----------
    public void Upsert(WidgetRecord w) => _db.Exec("""
      INSERT INTO widgets VALUES($id,$k,$n,$g,$b,$mk,$x,$y,$w,$h,$op,$mat,$mi,$cr,$ss,$ae,$vm,$v,$c,$cfg)
      ON CONFLICT(id) DO UPDATE SET kind=$k,name=$n,group_id=$g,box_id=$b,monitor_key=$mk,x=$x,y=$y,width=$w,
        height=$h,opacity=$op,material=$mat,material_intensity=$mi,corner_radius=$cr,style_scope=$ss,
        animation_effect=$ae,view_mode=$vm,is_visible=$v,is_collapsed=$c,config_json=$cfg;
      """,
      ("$id", w.Id), ("$k", (int)w.Kind), ("$n", w.Name), ("$g", w.GroupId), ("$b", w.BoxId),
      ("$mk", w.MonitorKey), ("$x", w.X), ("$y", w.Y), ("$w", w.Width), ("$h", w.Height),
      ("$op", w.Opacity), ("$mat", w.Material), ("$mi", w.MaterialIntensity), ("$cr", w.CornerRadius),
      ("$ss", (int)w.StyleScope), ("$ae", w.AnimationEffect), ("$vm", w.ViewMode),
      ("$v", w.IsVisible ? 1 : 0), ("$c", w.IsCollapsed ? 1 : 0), ("$cfg", w.ConfigJson));

    public List<WidgetRecord> Widgets() => _db.Query("SELECT * FROM widgets", r => new WidgetRecord
    {
        Id = r.GetString(0), Kind = (WidgetKind)r.GetInt32(1), Name = r.GetString(2), GroupId = r.GetStringOrNull(3),
        BoxId = r.GetStringOrNull(4), MonitorKey = r.GetString(5), X = r.GetDouble(6), Y = r.GetDouble(7),
        Width = r.GetDouble(8), Height = r.GetDouble(9), Opacity = r.GetDouble(10), Material = r.GetString(11),
        MaterialIntensity = r.GetDouble(12), CornerRadius = r.GetInt32(13), StyleScope = (StyleScope)r.GetInt32(14),
        AnimationEffect = r.GetString(15), ViewMode = r.GetString(16), IsVisible = r.GetInt32(17) != 0,
        IsCollapsed = r.GetInt32(18) != 0, ConfigJson = r.GetStringOrNull(19)
    });

    public void DeleteWidget(string id) => _db.Exec("DELETE FROM widgets WHERE id=$id", ("$id", id));

    // ---------- Todos / Notes ----------
    public void Upsert(TodoRecord t) => _db.Exec("""
      INSERT INTO todos VALUES($id,$ti,$no,$ln,$pr,$rp,$du,$rm,$dn,$ar,$ca,$co)
      ON CONFLICT(id) DO UPDATE SET title=$ti,note=$no,list_name=$ln,priority=$pr,repeat=$rp,due_at=$du,
        remind_minutes_before=$rm,is_done=$dn,is_archived=$ar,created_at=$ca,completed_at=$co;
      """,
      ("$id", t.Id), ("$ti", t.Title), ("$no", t.Note), ("$ln", t.ListName), ("$pr", t.Priority),
      ("$rp", t.Repeat), ("$du", t.DueAt?.ToString("o")), ("$rm", t.RemindMinutesBefore),
      ("$dn", t.IsDone ? 1 : 0), ("$ar", t.IsArchived ? 1 : 0), ("$ca", t.CreatedAt.ToString("o")),
      ("$co", t.CompletedAt?.ToString("o")));

    public List<TodoRecord> Todos(bool includeArchived = false) => _db.Query(
        includeArchived ? "SELECT * FROM todos ORDER BY priority DESC, due_at" : "SELECT * FROM todos WHERE is_archived=0 ORDER BY priority DESC, due_at",
        r => new TodoRecord
        {
            Id = r.GetString(0), Title = r.GetString(1), Note = r.GetStringOrNull(2), ListName = r.GetString(3),
            Priority = r.GetInt32(4), Repeat = r.GetString(5), DueAt = r.GetDateOrNull(6),
            RemindMinutesBefore = r.GetInt32(7), IsDone = r.GetInt32(8) != 0, IsArchived = r.GetInt32(9) != 0,
            CreatedAt = DateTimeOffset.Parse(r.GetString(10)), CompletedAt = r.GetDateOrNull(11)
        });

    public void DeleteTodo(string id) => _db.Exec("DELETE FROM todos WHERE id=$id", ("$id", id));

    public void Upsert(NoteRecord n) => _db.Exec("""
      INSERT INTO notes VALUES($id,$t,$b,$f,$p,$tg,$u)
      ON CONFLICT(id) DO UPDATE SET title=$t,body=$b,format=$f,is_pinned=$p,tags=$tg,updated_at=$u;
      """,
      ("$id", n.Id), ("$t", n.Title), ("$b", n.Body), ("$f", n.Format), ("$p", n.IsPinned ? 1 : 0),
      ("$tg", n.Tags), ("$u", n.UpdatedAt.ToString("o")));

    public List<NoteRecord> Notes() => _db.Query("SELECT * FROM notes ORDER BY is_pinned DESC, updated_at DESC",
        r => new NoteRecord
        {
            Id = r.GetString(0), Title = r.GetString(1), Body = r.GetString(2), Format = r.GetString(3),
            IsPinned = r.GetInt32(4) != 0, Tags = r.GetStringOrNull(5), UpdatedAt = DateTimeOffset.Parse(r.GetString(6))
        });

    public void DeleteNote(string id) => _db.Exec("DELETE FROM notes WHERE id=$id", ("$id", id));

    // ---------- Rules / runs ----------
    public void Upsert(RuleRecord r) => _db.Exec("""
      INSERT INTO rules VALUES($id,$n,$e,$p,$c,$a,$h)
      ON CONFLICT(id) DO UPDATE SET name=$n,enabled=$e,priority=$p,conditions_json=$c,actions_json=$a,hit_count=$h;
      """,
      ("$id", r.Id), ("$n", r.Name), ("$e", r.Enabled ? 1 : 0), ("$p", r.Priority),
      ("$c", r.ConditionsJson), ("$a", r.ActionsJson), ("$h", r.HitCount));

    public List<RuleRecord> Rules() => _db.Query("SELECT * FROM rules ORDER BY priority", r => new RuleRecord
    {
        Id = r.GetString(0), Name = r.GetString(1), Enabled = r.GetInt32(2) != 0, Priority = r.GetInt32(3),
        ConditionsJson = r.GetString(4), ActionsJson = r.GetString(5), HitCount = r.GetInt32(6)
    });

    public void DeleteRule(string id) => _db.Exec("DELETE FROM rules WHERE id=$id", ("$id", id));

    public void InsertRun(OrganizeRunRecord run) => _db.Exec("""
      INSERT INTO org_runs VALUES($id,$s,$f,$t,$m,$u,$j)
      ON CONFLICT(id) DO UPDATE SET started_at=$s,finished_at=$f,trigger=$t,moved_count=$m,undone=$u,moves_json=$j;
      """,
      ("$id", run.Id), ("$s", run.StartedAt.ToString("o")), ("$f", run.FinishedAt?.ToString("o")),
      ("$t", run.Trigger), ("$m", run.MovedCount), ("$u", run.Undone ? 1 : 0), ("$j", run.MovesJson));

    public List<OrganizeRunRecord> Runs(int limit = 30) => _db.Query(
        $"SELECT * FROM org_runs ORDER BY started_at DESC LIMIT {limit}", r => new OrganizeRunRecord
        {
            Id = r.GetString(0), StartedAt = DateTimeOffset.Parse(r.GetString(1)), FinishedAt = r.GetDateOrNull(2),
            Trigger = r.GetString(3), MovedCount = r.GetInt32(4), Undone = r.GetInt32(5) != 0, MovesJson = r.GetString(6)
        });

    // ---------- Pet / Usage / Power / Api ----------
    public PetRecord Pet()
    {
        var list = _db.Query("SELECT * FROM pets LIMIT 1", r => new PetRecord
        {
            Id = r.GetString(0), PackId = r.GetStringOrNull(1), Name = r.GetString(2), Scale = r.GetDouble(3),
            Opacity = r.GetDouble(4), X = r.GetDouble(5), Y = r.GetDouble(6), TopmostMode = r.GetString(7),
            KeyboardFollow = r.GetInt32(8) != 0, ClickReaction = r.GetInt32(9) != 0, SystemReaction = r.GetInt32(10) != 0,
            IdleReaction = r.GetInt32(11) != 0, TodoReminder = r.GetInt32(12) != 0, PomodoroEnabled = r.GetInt32(13) != 0,
            AiAskEnabled = r.GetInt32(14) != 0, FpsLimit = r.GetInt32(15), IsVisible = r.GetInt32(16) != 0
        });
        if (list.Count > 0) return list[0];
        var p = new PetRecord();
        SavePet(p);
        return p;
    }

    public void SavePet(PetRecord p) => _db.Exec("""
      INSERT INTO pets VALUES($id,$pk,$n,$s,$o,$x,$y,$tm,$kf,$cr,$sr,$ir,$tr,$pe,$ai,$fps,$v)
      ON CONFLICT(id) DO UPDATE SET pack_id=$pk,name=$n,scale=$s,opacity=$o,x=$x,y=$y,topmost_mode=$tm,
        keyboard_follow=$kf,click_reaction=$cr,system_reaction=$sr,idle_reaction=$ir,todo_reminder=$tr,
        pomodoro_enabled=$pe,ai_ask_enabled=$ai,fps_limit=$fps,is_visible=$v;
      """,
      ("$id", p.Id), ("$pk", p.PackId), ("$n", p.Name), ("$s", p.Scale), ("$o", p.Opacity), ("$x", p.X), ("$y", p.Y),
      ("$tm", p.TopmostMode), ("$kf", p.KeyboardFollow ? 1 : 0), ("$cr", p.ClickReaction ? 1 : 0),
      ("$sr", p.SystemReaction ? 1 : 0), ("$ir", p.IdleReaction ? 1 : 0), ("$tr", p.TodoReminder ? 1 : 0),
      ("$pe", p.PomodoroEnabled ? 1 : 0), ("$ai", p.AiAskEnabled ? 1 : 0), ("$fps", p.FpsLimit),
      ("$v", p.IsVisible ? 1 : 0));

    public void InsertUsage(UsageSessionRecord s) => _db.Exec(
        "INSERT INTO usage_sessions VALUES($id,$p,$t,$s,$e,$d)",
        ("$id", s.Id), ("$p", s.ProcessName), ("$t", s.WindowTitle), ("$s", s.StartAt.ToString("o")),
        ("$e", s.EndAt.ToString("o")), ("$d", s.DurationSeconds));

    public List<UsageSessionRecord> UsageSince(DateTimeOffset since) => _db.Query(
        "SELECT * FROM usage_sessions WHERE end_at >= $s ORDER BY start_at", r => new UsageSessionRecord
        {
            Id = r.GetString(0), ProcessName = r.GetString(1), WindowTitle = r.GetStringOrNull(2),
            StartAt = DateTimeOffset.Parse(r.GetString(3)), EndAt = DateTimeOffset.Parse(r.GetString(4)),
            DurationSeconds = r.GetInt64(5)
        }, ("$s", since.ToString("o")));

    public void ClearUsage() => _db.Exec("DELETE FROM usage_sessions");

    public PowerPlanRecord PowerPlan()
    {
        var list = _db.Query("SELECT * FROM power_plan LIMIT 1", r => new PowerPlanRecord
        {
            Id = r.GetString(0), Enabled = r.GetInt32(1) != 0, Trigger = (PowerTriggerKind)r.GetInt32(2),
            ClockTime = r.GetString(3), WeekdaysOnly = r.GetInt32(4) != 0, CountdownMinutes = r.GetInt32(5),
            IdleMinutes = r.GetInt32(6), WarnMinutesBefore = r.GetInt32(7), Action = (PowerActionKind)r.GetInt32(8),
            BackupBefore = r.GetInt32(9) != 0, DeferOnActivity = r.GetInt32(10) != 0
        });
        if (list.Count > 0) return list[0];
        var p = new PowerPlanRecord();
        SavePowerPlan(p);
        return p;
    }

    public void SavePowerPlan(PowerPlanRecord p) => _db.Exec("""
      INSERT INTO power_plan VALUES($id,$e,$t,$c,$w,$cd,$idle,$warn,$a,$b,$d)
      ON CONFLICT(id) DO UPDATE SET enabled=$e,trigger=$t,clock_time=$c,weekdays_only=$w,countdown_minutes=$cd,
        idle_minutes=$idle,warn_minutes_before=$warn,action=$a,backup_before=$b,defer_on_activity=$d;
      """,
      ("$id", p.Id), ("$e", p.Enabled ? 1 : 0), ("$t", (int)p.Trigger), ("$c", p.ClockTime),
      ("$w", p.WeekdaysOnly ? 1 : 0), ("$cd", p.CountdownMinutes), ("$idle", p.IdleMinutes),
      ("$warn", p.WarnMinutesBefore), ("$a", (int)p.Action), ("$b", p.BackupBefore ? 1 : 0),
      ("$d", p.DeferOnActivity ? 1 : 0));

    public List<ApiProviderRecord> Providers() => _db.Query("SELECT * FROM api_providers", r => new ApiProviderRecord
    {
        Id = r.GetString(0), Name = r.GetString(1), BaseUrl = r.GetString(2), Model = r.GetStringOrNull(3),
        KeyCipher = r.GetStringOrNull(4), ManualQuota = r.IsDBNull(5) ? null : r.GetDouble(5),
        QuotaWarnBelow = r.IsDBNull(6) ? null : r.GetDouble(6), Enabled = r.GetInt32(7) != 0
    });

    public void Upsert(ApiProviderRecord p) => _db.Exec("""
      INSERT INTO api_providers VALUES($id,$n,$u,$m,$k,$q,$w,$e)
      ON CONFLICT(id) DO UPDATE SET name=$n,base_url=$u,model=$m,key_cipher=$k,manual_quota=$q,quota_warn_below=$w,enabled=$e;
      """,
      ("$id", p.Id), ("$n", p.Name), ("$u", p.BaseUrl), ("$m", p.Model), ("$k", p.KeyCipher),
      ("$q", p.ManualQuota), ("$w", p.QuotaWarnBelow), ("$e", p.Enabled ? 1 : 0));

    public void DeleteProvider(string id) => _db.Exec("DELETE FROM api_providers WHERE id=$id", ("$id", id));

    public void InsertApiStat(ApiStatRecord s) => _db.Exec(
        "INSERT INTO api_stats VALUES($id,$p,$a,$sc,$l,$pu,$e)",
        ("$id", s.Id), ("$p", s.ProviderId), ("$a", s.At.ToString("o")), ("$sc", s.Success ? 1 : 0),
        ("$l", s.LatencyMs), ("$pu", s.Purpose), ("$e", s.ErrorKind));

    public List<ApiStatRecord> ApiStats(string providerId, DateTimeOffset since) => _db.Query(
        "SELECT * FROM api_stats WHERE provider_id=$p AND at >= $s", r => new ApiStatRecord
        {
            Id = r.GetString(0), ProviderId = r.GetString(1), At = DateTimeOffset.Parse(r.GetString(2)),
            Success = r.GetInt32(3) != 0, LatencyMs = r.GetInt64(4), Purpose = r.GetString(5),
            ErrorKind = r.GetStringOrNull(6)
        }, ("$p", providerId), ("$s", since.ToString("o")));

    // ---------- Journal（意图记录：断电/强杀后可续做或回滚） ----------
    public void Journal(string kind, object payload) => _db.Exec(
        "INSERT INTO journal VALUES($id,$k,$p,$c,$a)",
        ("$id", Guid.NewGuid().ToString("n")), ("$k", kind), ("$p", JsonSerializer.Serialize(payload, J)),
        ("$c", DateTimeOffset.Now.ToString("o")), ("$a", 0));

    public void CloseJournal(string id) => _db.Exec("UPDATE journal SET applied=1 WHERE id=$id", ("$id", id));
    public List<(string Id, string Kind, string Payload)> OpenJournals() => _db.Query(
        "SELECT id, kind, payload_json FROM journal WHERE applied=0", r => (r.GetString(0), r.GetString(1), r.GetString(2)));

    // ---------- 搜索索引（持久化，避免每次启动重扫全盘） ----------
    public void SaveSearchIndex(IEnumerable<Services.SearchService.IndexEntry> entries)
    {
        _db.Exec("DELETE FROM search_index;");
        _db.Exec("BEGIN;");
        var count = 0;
        foreach (var e in entries)
        {
            _db.Exec("INSERT OR REPLACE INTO search_index(path,is_dir,size,mtime) VALUES($p,$d,$s,$m)",
                ("$p", e.Path), ("$d", e.IsDirectory ? 1 : 0), ("$s", e.Size), ("$m", e.Modified.Ticks));
            if (++count % 2000 == 0)
            {
                _db.Exec("COMMIT;");
                _db.Exec("BEGIN;");
            }
        }
        _db.Exec("COMMIT;");
        Log.Info($"搜索索引已保存：{count} 条");
    }

    public List<Services.SearchService.IndexEntry> LoadSearchIndex() => _db.Query(
        "SELECT path,is_dir,size,mtime FROM search_index",
        r => new Services.SearchService.IndexEntry(r.GetString(0), r.GetInt32(1) != 0, r.GetInt64(2),
            new DateTime(r.GetInt64(3))));

    public static void EnsureSearchIndexTable(Db db) => db.Exec(
        "CREATE TABLE IF NOT EXISTS search_index(path TEXT PRIMARY KEY, is_dir INTEGER, size INTEGER, mtime INTEGER);");
}
