using System;
using System.Collections.Generic;
using System.Data.SQLite;
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
INSERT INTO Pets (OwnerUserId, Name, Species, Breed, Age, Notes)
VALUES (@owner, @name, @species, @breed, @age, @notes);
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("@owner", pet.OwnerUserId);
                command.Parameters.AddWithValue("@name", pet.Name);
                command.Parameters.AddWithValue("@species", (object)pet.Species ?? DBNull.Value);
                command.Parameters.AddWithValue("@breed", (object)pet.Breed ?? DBNull.Value);
                command.Parameters.AddWithValue("@age", pet.Age);
                command.Parameters.AddWithValue("@notes", (object)pet.Notes ?? DBNull.Value);
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
                command.Parameters.AddWithValue("@id", petId);
                command.ExecuteNonQuery();
            }
        }

        public List<Pet> GetByOwner(int ownerUserId)
        {
            var pets = new List<Pet>();
            using (var connection = _db.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Pets WHERE OwnerUserId = @owner ORDER BY Name COLLATE NOCASE;";
                command.Parameters.AddWithValue("@owner", ownerUserId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        pets.Add(Map(reader));
                }
            }
            return pets;
        }

        private static Pet Map(SQLiteDataReader reader)
        {
            return new Pet
            {
                Id = Convert.ToInt32(reader["Id"]),
                OwnerUserId = Convert.ToInt32(reader["OwnerUserId"]),
                Name = reader["Name"] as string,
                Species = reader["Species"] as string,
                Breed = reader["Breed"] as string,
                Age = Convert.ToInt32(reader["Age"]),
                Notes = reader["Notes"] as string
            };
        }
    }
}
