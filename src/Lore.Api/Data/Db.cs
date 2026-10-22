using System.Data;
using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Lore.Api.Data;

public sealed class Db
{
    readonly string _connectionString;

    static Db()
    {
        // SQLite has no native Guid/DateTimeOffset; store both as sortable TEXT.
        SqlMapper.RemoveTypeMap(typeof(Guid));
        SqlMapper.RemoveTypeMap(typeof(Guid?));
        SqlMapper.AddTypeHandler(new GuidHandler());
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset));
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset?));
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        SqlMapper.RemoveTypeMap(typeof(DateTime));
        SqlMapper.RemoveTypeMap(typeof(DateTime?));
        SqlMapper.AddTypeHandler(new DateTimeHandler());
    }

    /// <summary>Local wall-clock text; fixed width up to the fraction so string order == time order.</summary>
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFF";

    public Db(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            DefaultTimeout = 30, // also the SQLITE_BUSY retry window
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    public void Migrate(IEnumerable<(string Id, string Name)> volumes)
    {
        using var c = Open();
        c.Execute("PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS SchemaVersion(Version INTEGER NOT NULL)");
        var current = c.ExecuteScalar<long?>("SELECT MAX(Version) FROM SchemaVersion") ?? 0;

        var asm = Assembly.GetExecutingAssembly();
        var scripts = asm.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql"))
            .Select(n => (Name: n, Version: int.Parse(n.Split('.')[^2].Split('_')[0])))
            .OrderBy(s => s.Version);

        foreach (var s in scripts.Where(s => s.Version > current))
        {
            using var reader = new StreamReader(asm.GetManifestResourceStream(s.Name)!);
            using var tx = c.BeginTransaction();
            c.Execute(reader.ReadToEnd(), transaction: tx);
            c.Execute("INSERT INTO SchemaVersion(Version) VALUES (@v)", new { v = s.Version }, tx);
            tx.Commit();
        }

        foreach (var v in volumes)
            c.Execute("INSERT OR IGNORE INTO StorageVolume(Id, Name) VALUES (@Id, @Name)", new { v.Id, v.Name });
    }

    sealed class GuidHandler : SqlMapper.TypeHandler<Guid>
    {
        public override void SetValue(IDbDataParameter p, Guid value) { p.DbType = DbType.String; p.Value = value.ToString("D"); }
        public override Guid Parse(object value) => Guid.Parse((string)value);
    }

    sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter p, DateTimeOffset value) { p.DbType = DbType.String; p.Value = value.ToUniversalTime().ToString("O"); }
        public override DateTimeOffset Parse(object value) => DateTimeOffset.Parse((string)value, System.Globalization.CultureInfo.InvariantCulture);
    }

    sealed class DateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(IDbDataParameter p, DateTime value) { p.DbType = DbType.String; p.Value = value.ToString(DateTimeFormat, System.Globalization.CultureInfo.InvariantCulture); }
        public override DateTime Parse(object value) => DateTime.Parse((string)value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
