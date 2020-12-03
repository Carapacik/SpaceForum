namespace SpaceForum.Application.Messaging;

public sealed record ConversationView(Guid Id, string Subject, DateTimeOffset LastMessageAt, string Participants, int UnreadCount);
public sealed record MessageView(Guid Id, string AuthorLogin, string AuthorDisplayName, string Body, DateTimeOffset CreatedAt);
public sealed record ConversationDetailView(Guid Id, string Subject, IReadOnlyList<MessageView> Messages);

public interface IMessagingRepository
{
    Task<IReadOnlyList<ConversationView>> ListAsync(Guid memberId, CancellationToken cancellationToken);
    Task<ConversationDetailView?> GetAsync(Guid conversationId, Guid memberId, CancellationToken cancellationToken);
    Task<Guid?> CreateAsync(Guid creatorId, Guid recipientId, string subject, string body, DateTimeOffset createdAt, CancellationToken cancellationToken);
    Task<bool> SendAsync(Guid conversationId, Guid authorId, string body, DateTimeOffset createdAt, CancellationToken cancellationToken);
}

public sealed class MessagingService(IMessagingRepository repository, TimeProvider timeProvider)
{
    public Task<IReadOnlyList<ConversationView>> ListAsync(Guid memberId, CancellationToken cancellationToken)=>repository.ListAsync(memberId,cancellationToken);
    public Task<ConversationDetailView?> GetAsync(Guid conversationId,Guid memberId,CancellationToken cancellationToken)=>repository.GetAsync(conversationId,memberId,cancellationToken);
    public Task<Guid?> CreateAsync(Guid creatorId,Guid recipientId,string subject,string body,CancellationToken cancellationToken)=>subject.Trim().Length is < 3 or > 160||body.Trim().Length is < 2 or > 10000?Task.FromResult<Guid?>(null):repository.CreateAsync(creatorId,recipientId,subject.Trim(),body.Trim(),timeProvider.GetUtcNow(),cancellationToken);
    public Task<bool> SendAsync(Guid conversationId,Guid authorId,string body,CancellationToken cancellationToken)=>body.Trim().Length is < 2 or > 10000?Task.FromResult(false):repository.SendAsync(conversationId,authorId,body.Trim(),timeProvider.GetUtcNow(),cancellationToken);
}
