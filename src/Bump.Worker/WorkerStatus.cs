namespace Bump.Worker;

public class WorkerStatus
{
    public DateTime? LastPollUtc { get; private set; }
    public DateTime? LastServiceTickUtc { get; private set; }
    public DateTime? LastAnnouncementTickUtc { get; private set; }
    public string? LastError { get; private set; }

    public void RecordPoll() => LastPollUtc = DateTime.UtcNow;
    public void RecordServiceTick() => LastServiceTickUtc = DateTime.UtcNow;
    public void RecordAnnouncementTick() => LastAnnouncementTickUtc = DateTime.UtcNow;
    public void RecordError(Exception ex) => LastError = ex.Message;
    public void ClearError() => LastError = null;
}
