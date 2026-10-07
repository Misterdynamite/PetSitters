using System;
using System.Collections.Generic;
using System.Data.Common;
using PetSitters.Models;

namespace PetSitters.Data
{
    /// <summary>Reads and writes an owner's <see cref="Pet"/> rows.</summary>
    public class PetRepository
    {
        private readonly Database _db;

        public PetRepository(Database db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public Pet Insert(Pet pet)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT INTO Pets (OwnerUserId, Name, Species, Breed, Age, AgeMonths, ImagePath, Notes)
VALUES (@owner, @name, @species, @breed, @age, @ageMonths, @image, @notes);
" + _db.Dialect.SelectLastInsertId;
                command.AddParameter("@owner", pet.OwnerUserId);
                command.AddParameter("@name", pet.Name);
                command.AddParameter("@species", (object)pet.Species ?? DBNull.Value);
                command.AddParameter("@breed", (object)pet.Breed ?? DBNull.Value);
                command.AddParameter("@age", pet.Age);
                command.AddParameter("@ageMonths", pet.AgeMonths);
                command.AddParameter("@notes", (object)pet.Notes ?? DBNull.Value);
                command.AddParameter("@image", (object)pet.ImagePath ?? DBNull.Value);
                pet.Id = Convert.ToInt32(command.ExecuteScalar());
                return pet;
            }
        }

        public void Delete(int petId)
        {
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM Pets WHERE Id = @id;";
                command.AddParameter("@id", petId);
                command.ExecuteNonQuery();
            }
        }

        public List<Pet> GetByOwner(int ownerUserId)
        {
            var pets = new List<Pet>();
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Pets WHERE OwnerUserId = @owner ORDER BY " + _db.Dialect.OrderByIgnoringCase("Name") + ", Id;";
                command.AddParameter("@owner", ownerUserId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        pets.Add(Map(reader));
                }
            }
            return pets;
        }

        /// <summary>Every Pets column, for aliased JOIN selects (see BookingRepository.GetDetailsForOwner).</summary>
        internal static readonly string[] Columns =
            { "Id", "OwnerUserId", "Name", "Species", "Breed", "Age", "AgeMonths", "Notes", "ImagePath" };

        private static Pet Map(DbDataReader reader)
        {
            return MapJoined(reader, string.Empty);
        }

        /// <summary>
        /// Maps a pet whose columns were selected with <paramref name="prefix"/>
        /// (empty for a plain SELECT *); null if a LEFT JOIN found no pet.
        /// </summary>
        internal static Pet MapJoined(DbDataReader reader, string prefix)
        {
            if (reader.IsMissing(prefix + "Id"))
                return null;

            return new Pet
            {
                Id = Convert.ToInt32(reader[prefix + "Id"]),
                OwnerUserId = Convert.ToInt32(reader[prefix + "OwnerUserId"]),
                Name = reader[prefix + "Name"] as string,
                Species = reader[prefix + "Species"] as string,
                Breed = reader[prefix + "Breed"] as string,
                Age = Convert.ToInt32(reader[prefix + "Age"]),
                AgeMonths = Convert.ToInt32(reader[prefix + "AgeMonths"]),
                Notes = reader[prefix + "Notes"] as string,
                // Only trusted if it points into this PC's own image folder (see LocalImages).
                ImagePath = LocalImages.TrustedPathOrNull(reader[prefix + "ImagePath"] as string)
            };
        }
    }
}
