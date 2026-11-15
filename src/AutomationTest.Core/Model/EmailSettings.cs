using System.Runtime.Serialization;

namespace AutomationTest.Core.Model
{
    public enum EmailKind
    {
        /// <summary>Sends a message.</summary>
        Send,
        /// <summary>Reads the messages that match the filters into a table.</summary>
        Read,
        /// <summary>Marks the messages that match the filters as read or unread.</summary>
        Mark,
        /// <summary>Deletes the messages that match the filters (to the Trash folder, or for good).</summary>
        Delete
    }

    public enum EmailStatus
    {
        Any,
        Unread,
        Read
    }

    public enum EmailMarkAs
    {
        Read,
        Unread
    }

    /// <summary>
    /// Settings of an Email action. The mail account is not here: <see cref="Profile"/> is the name of a profile kept by the
    /// program (with its servers, user and password), so a script file never holds a password.
    /// </summary>
    [DataContract]
    public sealed class EmailSettings
    {
        public EmailKind Kind { get; set; }

        /// <summary>The name of the profile (Email profiles, the mail icon next to Category).</summary>
        [DataMember(Order = 2)] public string Profile { get; set; } = "";

        // ---- Send
        [DataMember(Order = 3)] public string To { get; set; } = "";
        [DataMember(Order = 4)] public string Cc { get; set; } = "";
        [DataMember(Order = 5)] public string Bcc { get; set; } = "";
        /// <summary>To, Cc and Bcc are expressions.</summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public bool ToIsExpression { get; set; }

        /// <summary>Send: the subject. Read, Mark, Delete: the subject must contain this text.</summary>
        [DataMember(Order = 7)] public string Subject { get; set; } = "";
        [DataMember(Order = 8, EmitDefaultValue = false)] public bool SubjectIsExpression { get; set; }

        [DataMember(Order = 9)] public string Body { get; set; } = "";
        [DataMember(Order = 10, EmitDefaultValue = false)] public bool BodyIsExpression { get; set; }
        /// <summary>The body is HTML (otherwise plain text).</summary>
        [DataMember(Order = 11, EmitDefaultValue = false)] public bool BodyIsHtml { get; set; }

        /// <summary>Send: the files to attach, one path per line.</summary>
        [DataMember(Order = 12)] public string Attachments { get; set; } = "";
        [DataMember(Order = 13, EmitDefaultValue = false)] public bool AttachmentsIsExpression { get; set; }

        // ---- Read, Mark, Delete: which messages
        /// <summary>The mail folder; empty means INBOX.</summary>
        [DataMember(Order = 14)] public string Folder { get; set; } = "";
        /// <summary>The sender must contain this text.</summary>
        [DataMember(Order = 15)] public string From { get; set; } = "";
        /// <summary>Received on or after this day (yyyy-MM-dd); empty = no limit.</summary>
        [DataMember(Order = 16)] public string Since { get; set; } = "";
        /// <summary>Received before this day (yyyy-MM-dd); empty = no limit.</summary>
        [DataMember(Order = 17)] public string Before { get; set; } = "";
        [DataMember(Order = 18, EmitDefaultValue = false)] public bool DatesAreExpressions { get; set; }
        public EmailStatus Status { get; set; }
        /// <summary>At most this many messages (the newest ones).</summary>
        [DataMember(Order = 20)] public int Max { get; set; } = 10;

        /// <summary>Read: mark what was read as read.</summary>
        [DataMember(Order = 21, EmitDefaultValue = false)] public bool MarkRead { get; set; }
        /// <summary>Read: the folder to save the attachments of the messages in (empty = do not save).</summary>
        [DataMember(Order = 22)] public string SaveTo { get; set; } = "";
        public EmailMarkAs MarkAs { get; set; }
        /// <summary>Delete: delete for good instead of moving to the Trash folder.</summary>
        [DataMember(Order = 24, EmitDefaultValue = false)] public bool Permanent { get; set; }

        /// <summary>Read: the variable that receives the messages (a table).</summary>
        [DataMember(Order = 25)] public string Id { get; set; } = "";

        /// <summary>How long to wait for the mail server, in milliseconds.</summary>
        [DataMember(Order = 26)] public long TimeMs { get; set; } = 60000;

        /// <summary>How long to wait after the action before the next one starts (milliseconds).</summary>
        [DataMember(Order = 27, EmitDefaultValue = false)] public long DelayMs { get; set; }

        [DataMember(Name = "Kind", Order = 1)]
        private string KindName
        {
            get { return Kind.ToString(); }
            set { Kind = ActionNode.Parse(value, EmailKind.Send); }
        }

        [DataMember(Name = "Status", Order = 19)]
        private string StatusName
        {
            get { return Status.ToString(); }
            set { Status = ActionNode.Parse(value, EmailStatus.Any); }
        }

        [DataMember(Name = "MarkAs", Order = 23)]
        private string MarkAsName
        {
            get { return MarkAs.ToString(); }
            set { MarkAs = ActionNode.Parse(value, EmailMarkAs.Read); }
        }

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Profile = To = Cc = Bcc = Subject = Body = Attachments = Folder = From = Since = Before = SaveTo = Id = "";
            Max = 10;
            TimeMs = 60000;
        }

        public EmailSettings Clone()
        {
            return (EmailSettings)MemberwiseClone();
        }

        internal void Normalize()
        {
            if (Profile == null) Profile = "";
            if (To == null) To = "";
            if (Cc == null) Cc = "";
            if (Bcc == null) Bcc = "";
            if (Subject == null) Subject = "";
            if (Body == null) Body = "";
            if (Attachments == null) Attachments = "";
            if (Folder == null) Folder = "";
            if (From == null) From = "";
            if (Since == null) Since = "";
            if (Before == null) Before = "";
            if (SaveTo == null) SaveTo = "";
            if (Id == null) Id = "";
            if (Max < 1) Max = 1;
            if (TimeMs < 1000) TimeMs = 60000;
            if (DelayMs < 0) DelayMs = 0;
        }
    }
}
