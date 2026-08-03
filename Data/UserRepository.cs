using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using PetSitters.Models;

namespace PetSitters.Data
{
    /// <summary>Reads and writes <see cref="User"/> rows.</summary>
    public class UserRepository
    {
        private readonly Database _db;

        public UserRepository(Database db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>True if an account already uses this email (case-insensitive).</summary>
        public bool EmailExists(string email)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(1) FROM Users WHERE Email = @email;";
                command.Parameters.AddWithValue("@email", email);
                long count = Convert.ToInt64(command.ExecuteScalar());
                return count > 0;
            }
        }

        /// <summary>Inserts a new user and returns it with its generated Id.</summary>
        public User Insert(User user)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT INTO Users (Email, PasswordHash, PasswordSalt, Role, FullName, Phone, Location, CreatedUtc)
VALUES (@email, @hash, @salt, @role, @name, @phone, @location, @created);
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("@email", user.Email);
                command.Parameters.AddWithValue("@hash", user.PasswordHash);
                command.Parameters.AddWithValue("@salt", user.PasswordSalt);
                command.Parameters.AddWithValue("@role", (int)user.Role);
                command.Parameters.AddWithValue("@name", user.FullName);
                command.Parameters.AddWithValue("@phone", (object)user.Phone ?? DBNull.Value);
                command.Parameters.AddWithValue("@location", (object)user.Location ?? DBNull.Value);
                command.Parameters.AddWithValue("@created", user.CreatedUtc.ToString("o", CultureInfo.InvariantCulture));

                user.Id = Convert.ToInt32(command.ExecuteScalar());
                return user;
            }
        }

        /// <summary>Updates the editable personal details of an existing user.</summary>
        public void UpdateDetails(User user)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
UPDATE Users
SET FullName = @name, Phone = @phone, Location = @location
WHERE Id = @id;";
                command.Parameters.AddWithValue("@name", user.FullName);
                command.Parameters.AddWithValue("@phone", (object)user.Phone ?? DBNull.Value);
                command.Parameters.AddWithValue("@location", (object)user.Location ?? DBNull.Value);
                command.Parameters.AddWithValue("@id", user.Id);
                command.ExecuteNonQuery();
            }
        }

        public User FindByEmail(string email)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Users WHERE Email = @email LIMIT 1;";
                command.Parameters.AddWithValue("@email", email);
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? Map(reader) : null;
                }
            }
        }

        public User FindById(int id)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Users WHERE Id = @id LIMIT 1;";
                command.Parameters.AddWithValue("@id", id);
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? Map(reader) : null;
                }
            }
        }

        /// <summary>All users of a given role, ordered by name.</summary>
        public List<User> GetByRole(UserRole role)
        {
            var users = new List<User>();
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Users WHERE Role = @role ORDER BY FullName COLLATE NOCASE;";
                command.Parameters.AddWithValue("@role", (int)role);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        users.Add(Map(reader));
                }
            }
            return users;
        }

        private static User Map(SQLiteDataReader reader)
        {
            return new User
            {
                Id = Convert.ToInt32(reader["Id"]),
                Email = reader["Email"] as string,
                PasswordHash = reader["PasswordHash"] as string,
                PasswordSalt = reader["PasswordSalt"] as string,
                Role = (UserRole)Convert.ToInt32(reader["Role"]),
                FullName = reader["FullName"] as string,
                Phone = reader["Phone"] as string,
                Location = reader["Location"] as string,
                CreatedUtc = DateTime.Parse((string)reader["CreatedUtc"], CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)
            };
        }
    }
}
