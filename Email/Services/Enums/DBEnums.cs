namespace Email.Services.Enums
{

    //enum for DB field TourEmailInbox 
    public class DBEnums
    {
        public enum TourEmailInboxProcessingStatus
        {
            Processed = 0,
            NonBooking = 1,
            Failed = 2,
            Pending = 3

        }
    }
}
