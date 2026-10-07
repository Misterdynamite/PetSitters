using System;

namespace PetSitters.Models
{
    /// <summary>One message in a booking's chat (FR-O5 / FR-S5).</summary>
    public class ChatMessage
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public int SenderUserId { get; set; }
        public string MessageText { get; set; }
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// The sender's display name. Not stored: filled in only by
        /// ChatRepository.GetForBookingWithSenderNames (a JOIN), so the chat
        /// panel doesn't look up each sender separately.
        /// </summary>
        public string SenderName { get; set; }
    }
}
