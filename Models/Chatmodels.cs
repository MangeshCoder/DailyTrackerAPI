using System.ComponentModel.DataAnnotations;

namespace DailyTrackerAPI.Models
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  CHAT SYSTEM — DATABASE MODELS
    //
    //  Table design:
    //
    //   Conversations           ← "room" for a chat (Direct or Group)
    //     └── ConversationMembers ← who belongs to each conversation
    //     └── ChatMessages        ← messages inside the conversation
    //           └── MessageReadReceipts  ← who read each message and when
    //           └── MessageReactions    ← 👍❤️😂 reactions per message per user
    //
    //  One-to-one chats: ConversationType = "Direct", exactly 2 members.
    //  Group chats:       ConversationType = "Group",  unlimited members.
    // ═══════════════════════════════════════════════════════════════════════════

    // ─── 1. Conversation ──────────────────────────────────────────────────────
    public class Conversation
    {
        public int Id { get; set; }

        /// <summary>"Direct" for 1:1 chat, "Group" for group chat</summary>
        [Required, MaxLength(20)]
        public string Type { get; set; } = "Direct";

        /// <summary>Only meaningful for Group chats</summary>
        [MaxLength(100)]
        public string? GroupName { get; set; }

        /// <summary>Emoji or image URL for the group avatar (optional)</summary>
        [MaxLength(500)]
        public string? GroupAvatar { get; set; }

        /// <summary>Who created this conversation (admin for group, initiator for direct)</summary>
        public int CreatedByUserId { get; set; }
        public virtual User CreatedBy { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Cached to show in sidebar without querying messages</summary>
        public DateTime? LastMessageAt { get; set; }

        [MaxLength(500)]
        public string? LastMessagePreview { get; set; }

        public bool IsActive { get; set; } = true;

        // Navigation
        public virtual ICollection<ConversationMember> Members { get; set; } = new List<ConversationMember>();
        public virtual ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    // ─── 2. ConversationMember ────────────────────────────────────────────────
    public class ConversationMember
    {
        public int Id { get; set; }

        public int ConversationId { get; set; }
        public virtual Conversation Conversation { get; set; } = null!;

        public int UserId { get; set; }
        public virtual User User { get; set; } = null!;

        /// <summary>Member, Admin (group admins can add/remove members)</summary>
        [MaxLength(20)]
        public string Role { get; set; } = "Member";

        /// <summary>When this user joined the conversation</summary>
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        /// <summary>True if the user has left (soft-delete so history is preserved)</summary>
        public bool HasLeft { get; set; } = false;

        public DateTime? LeftAt { get; set; }

        /// <summary>Timestamp of the last message this user has read in the conversation.
        /// Used to calculate unread counts efficiently.</summary>
        public DateTime? LastReadAt { get; set; }

        /// <summary>Allow muting notifications per conversation</summary>
        public bool IsMuted { get; set; } = false;
    }

    // ─── 3. ChatMessage ───────────────────────────────────────────────────────
    public class ChatMessage
    {
        public int Id { get; set; }

        public int ConversationId { get; set; }
        public virtual Conversation Conversation { get; set; } = null!;

        public int? SenderId { get; set; }
        public virtual User? Sender { get; set; } = null!;

        /// <summary>The actual message content</summary>
        [Required, MaxLength(4000)]
        public string Content { get; set; } = string.Empty;

        /// <summary>"Text" | "Image" | "File" | "System" (e.g. "Alice added Bob to group")</summary>
        [MaxLength(20)]
        public string MessageType { get; set; } = "Text";

        /// <summary>File or image URL if MessageType != Text</summary>
        [MaxLength(1000)]
        public string? AttachmentUrl { get; set; }

        [MaxLength(200)]
        public string? AttachmentName { get; set; }

        /// <summary>Threaded reply — points to the parent message ID</summary>
        public int? ReplyToMessageId { get; set; }
        public virtual ChatMessage? ReplyToMessage { get; set; }

        /// <summary>Soft-delete flag. Content replaced with "This message was deleted."</summary>
        public bool IsDeleted { get; set; } = false;

        /// <summary>Was the message edited after sending?</summary>
        public bool IsEdited { get; set; } = false;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public DateTime? EditedAt { get; set; }

        // Navigation
        public virtual ICollection<MessageReaction> Reactions { get; set; } = new List<MessageReaction>();
        public virtual ICollection<MessageReadReceipt> ReadReceipts { get; set; } = new List<MessageReadReceipt>();
    }

    // ─── 4. MessageReadReceipt ────────────────────────────────────────────────
    public class MessageReadReceipt
    {
        public int Id { get; set; }

        public int MessageId { get; set; }
        public virtual ChatMessage Message { get; set; } = null!;

        public int UserId { get; set; }
        public virtual User User { get; set; } = null!;

        public DateTime ReadAt { get; set; } = DateTime.UtcNow;
    }

    // ─── 5. MessageReaction ───────────────────────────────────────────────────
    public class MessageReaction
    {
        public int Id { get; set; }

        public int MessageId { get; set; }
        public virtual ChatMessage Message { get; set; } = null!;

        public int UserId { get; set; }
        public virtual User User { get; set; } = null!;

        /// <summary>Unicode emoji: "👍" "❤️" "😂" "😮" "😢" "🔥"</summary>
        [Required, MaxLength(10)]
        public string Emoji { get; set; } = string.Empty;

        public DateTime ReactedAt { get; set; } = DateTime.UtcNow;
    }
}
