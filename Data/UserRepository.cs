using System;
using System.Collections.Generic;
using System.Data.Common;
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

        /// <summary>True if an account of any role already uses this email (case-insensitive).</summary>
        public bool EmailExists(string email)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(1) FROM Users WHERE Email = @email;";
                command.AddParameter("@email", email);
                long count = Convert.ToInt64(command.ExecuteScalar());
                return count > 0;
            }
        }

        /// <summary>
        /// True if an account of this specific role already uses this email
        /// (case-insensitive). REQ-GR-06: an email may hold one account per role.
        /// </summary>
        public bool EmailExists(string email, UserRole role)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(1) FROM Users WHERE Email = @email AND Role = @role;";
                command.AddParameter("@email", email);
                command.AddParameter("@role", (int)role);
                long count = Convert.ToInt64(command.ExecuteScalar());
                return count > 0;
            }
        }

        /// <summary>
        /// Every account using this email, ordered by Id: at most one Owner and
        /// one Sitter (REQ-GR-06). Login uses this to decide which account is meant.
        /// </summary>
        public List<User> FindAllByEmail(string email)
        {
            var users = new List<User>();
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Users WHERE Email = @email ORDER BY Id;";
                command.AddParameter("@email", email);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        users.Add(Map(reader));
                }
            }
            return users;
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
" + _db.Dialect.SelectLastInsertId;
                command.AddParameter("@email", user.Email);
                command.AddParameter("@hash", user.PasswordHash);
                command.AddParameter("@salt", user.PasswordSalt);
                command.AddParameter("@role", (int)user.Role);
                command.AddParameter("@name", user.FullName);
                command.AddParameter("@phone", (object)user.Phone ?? DBNull.Value);
                command.AddParameter("@location", (object)user.Location ?? DBNull.Value);
                command.AddParameter("@created", user.CreatedUtc.ToString("o", CultureInfo.InvariantCulture));

                user.Id = Convert.ToInt32(command.ExecuteScalar());
                return user;
            }
        }

        /// <summary>
        /// Updates the editable personal details (name, phone, location) of an
        /// existing user. Deliberately NOT the profile picture: the path read back
        /// on another PC is "no image" (see LocalImages), so writing it back here
        /// would erase the picture the user set on their other computer. Use
        /// <see cref="UpdateProfileImage"/> for that.
        /// </summary>
        public void UpdateDetails(User user)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
UPDATE Users
SET FullName = @name, Phone = @phone, Location = @location
WHERE Id = @id;";
                command.AddParameter("@name", user.FullName);
                command.AddParameter("@phone", (object)user.Phone ?? DBNull.Value);
                command.AddParameter("@location", (object)user.Location ?? DBNull.Value);
                command.AddParameter("@id", user.Id);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Sets the user's profile picture path (only when they import a new picture).</summary>
        public void UpdateProfileImage(int userId, string imagePath)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE Users SET ProfileImagePath = @image WHERE Id = @id;";
                command.AddParameter("@image", (object)imagePath ?? DBNull.Value);
                command.AddParameter("@id", userId);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// The first (lowest Id) account using this email, or null. Since
        /// REQ-GR-06 an email can have two accounts, so anything that must pick
        /// the right one (e.g. login) uses <see cref="FindAllByEmail"/> instead.
        /// </summary>
        public User FindByEmail(string email)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Users WHERE Email = @email ORDER BY Id LIMIT 1;";
                command.AddParameter("@email", email);
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
                command.AddParameter("@id", id);
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
                command.CommandText = "SELECT * FROM Users WHERE Role = @role ORDER BY " + _db.Dialect.OrderByIgnoringCase("FullName") + ", Id;";
                command.AddParameter("@role", (int)role);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        users.Add(Map(reader));
                }
            }
            return users;
        }

        /// <summary>
        /// The columns safe to load when a user is JOINed into another query
        /// (e.g. a booking's owner and sitter): everything except the password
        /// hash and salt, which no screen needs about another person.
        /// </summary>
        internal static readonly string[] PublicColumns =
            { "Id", "Email", "Role", "FullName", "Phone", "Location", "ProfileImagePath", "CreatedUtc" };

        /// <summary>
        /// Every sitter with their profile (null if not filled in yet), ordered
        /// by name, in ONE query. The Find Sitters list used to load the
        /// profiles one sitter at a time (1 + N round trips).
        /// </summary>
        public List<SitterListing> GetSittersWithProfiles()
        {
            var listings = new List<SitterListing>();
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT " + DbCommandExtensions.AliasedColumns("u", "u_", PublicColumns) + ", " +
                    DbCommandExtensions.AliasedColumns("sp", "sp_", SitterProfileRepository.Columns) +
                    " FROM Users u LEFT JOIN SitterProfiles sp ON sp.UserId = u.Id" +
                    " WHERE u.Role = @role" +
                    " ORDER BY " + _db.Dialect.OrderByIgnoringCase("u.FullName") + ", u.Id;";
                command.AddParameter("@role", (int)UserRole.Sitter);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        listings.Add(new SitterListing
                        {
                            Sitter = MapJoined(reader, "u_"),
                            Profile = SitterProfileRepository.MapJoined(reader, "sp_"),
                        });
                }
            }
            return listings;
        }

        /// <summary>
        /// Maps a user whose <see cref="PublicColumns"/> were selected with
        /// <paramref name="prefix"/>; null if the LEFT JOIN found no user.
        /// PasswordHash/PasswordSalt stay null.
        /// </summary>
        internal static User MapJoined(DbDataReader reader, string prefix)
        {
            if (reader.IsMissing(prefix + "Id"))
                return null;

            return new User
            {
                Id = Convert.ToInt32(reader[prefix + "Id"]),
                Email = reader[prefix + "Email"] as string,
                Role = (UserRole)Convert.ToInt32(reader[prefix + "Role"]),
                FullName = reader[prefix + "FullName"] as string,
                Phone = reader[prefix + "Phone"] as string,
                Location = reader[prefix + "Location"] as string,
                CreatedUtc = DateTime.Parse((string)reader[prefix + "CreatedUtc"], CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                ProfileImagePath = LocalImages.TrustedPathOrNull(reader[prefix + "ProfileImagePath"] as string)
            };
        }

        private static User Map(DbDataReader reader)
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
                    DateTimeStyles.RoundtripKind),
                // Only trusted if it points into this PC's own image folder (see LocalImages).
                ProfileImagePath = LocalImages.TrustedPathOrNull(reader["ProfileImagePath"] as string)
            };
        }
    }
}
