using System;
using System.Data.Common;

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
    }
}
