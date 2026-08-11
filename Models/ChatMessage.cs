using System;

namespace PetSitters.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public int SenderUserId { get; set; }
        public string MessageText { get; set; }
        public DateTime CreatedUtc { get; set; }
    }
}
