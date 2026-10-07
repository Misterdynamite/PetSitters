using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;

namespace PetSitters.Data
{
    /// <summary>Provider-neutral helpers shared by the repositories.</summary>
    internal static class DbCommandExtensions
    {
        /// <summary>
        /// Adds a named parameter. The generic <see cref="DbParameterCollection"/>
        /// has no AddWithValue (only the concrete SQLite/MySQL collections do), so
        /// this is the portable equivalent. A null value is stored as SQL NULL.
        /// </summary>
        public static void AddParameter(this DbCommand command, string name, object value)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        /// <summary>
        /// "a.Id AS p_Id, a.Name AS p_Name, ..." for a JOIN: every column of a
        /// joined table is given a prefix, so several tables' columns (all
        /// called Id, Name, ...) can be read from one result row without clashing.
        /// Column names are hard-coded literals from our own schema, never user input.
        /// </summary>
        public static string AliasedColumns(string tableAlias, string prefix, IEnumerable<string> columns)
        {
            return string.Join(", ", columns.Select(c => tableAlias + "." + c + " AS " + prefix + c));
        }

        /// <summary>True when a LEFT JOIN found no row for the table whose id column is <paramref name="idColumn"/>.</summary>
        public static bool IsMissing(this DbDataReader reader, string idColumn)
        {
            return reader[idColumn] == DBNull.Value;
        }
    }
}
