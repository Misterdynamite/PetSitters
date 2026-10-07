using System;
using System.Linq;

namespace PetSitters.Data
{
    /// <summary>Which database engine a <see cref="Database"/> talks to.</summary>
    public enum DatabaseProvider
    {
        /// <summary>Local file at %AppData%\PetSitters\petsitters.db (offline fallback, and the test default).</summary>
        Sqlite,

        /// <summary>Shared cloud database configured by DATABASE_URL (the primary store when reachable).</summary>
        MySql
    }

    /// <summary>
    /// The few SQL fragments that differ between SQLite and MySQL. Everything
    /// else the repositories write is portable: parameterised statements, LIMIT,
    /// single-quoted literals only. Double-quoted strings are avoided because the
    /// target MySQL server runs with ANSI_QUOTES, where "x" means an identifier.
    /// Keeping the differences here means the repositories hold one copy of each
    /// query for both engines.
    /// </summary>
    internal sealed class SqlDialect
    {
        public static readonly SqlDialect Sqlite = new SqlDialect(DatabaseProvider.Sqlite);
        public static readonly SqlDialect MySql = new SqlDialect(DatabaseProvider.MySql);

        private readonly DatabaseProvider _provider;

        private SqlDialect(DatabaseProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Statement appended after an INSERT in the same command, returning the
        /// new row's id. Both engines scope it to the connection, so concurrent
        /// users can't see each other's ids.
        /// </summary>
        public string SelectLastInsertId
        {
            get { return _provider == DatabaseProvider.Sqlite ? "SELECT last_insert_rowid();" : "SELECT LAST_INSERT_ID();"; }
        }

        /// <summary>
        /// An ORDER BY term that sorts text ignoring case. SQLite compares
        /// case-sensitively unless told otherwise. The MySQL tables are created with
        /// a case-insensitive collation (utf8mb4_0900_as_ci), so the plain column already sorts that way.
        /// </summary>
        public string OrderByIgnoringCase(string column)
        {
            return _provider == DatabaseProvider.Sqlite ? column + " COLLATE NOCASE" : column;
        }

        /// <summary>
        /// The clause that turns a plain INSERT ... VALUES (...) into an upsert
        /// keyed on <paramref name="conflictColumn"/>, updating
        /// <paramref name="updateColumns"/> from the attempted row. MySQL uses the
        /// 8.0.19+ row-alias form (the older VALUES() form is deprecated).
        /// </summary>
        public string Upsert(string conflictColumn, params string[] updateColumns)
        {
            if (updateColumns == null || updateColumns.Length == 0)
                throw new ArgumentException("At least one column to update is required.", nameof(updateColumns));

            if (_provider == DatabaseProvider.Sqlite)
                return " ON CONFLICT(" + conflictColumn + ") DO UPDATE SET " +
                       string.Join(", ", updateColumns.Select(c => c + " = excluded." + c));

            // In MySQL the conflict target is whichever UNIQUE/PRIMARY key the row hits.
            return " AS new ON DUPLICATE KEY UPDATE " +
                   string.Join(", ", updateColumns.Select(c => c + " = new." + c));
        }
    }
}
