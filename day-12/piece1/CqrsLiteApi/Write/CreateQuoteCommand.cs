using CqrsLiteApi.Data;
using CqrsLiteApi.Domain;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CqrsLiteApi.Write;

// The WRITE side: a command, its validator, and its handler. Nothing here
// knows or cares what any screen looks like - it only knows what a valid
// Quote is and how to persist one against the normalized domain model.
public record CreateQuoteCommand(int AuthorId, string Text) : IRequest<int>;

public class CreateQuoteCommandValidator : AbstractValidator<CreateQuoteCommand>
{
    public CreateQuoteCommandValidator()
    {
        RuleFor(c => c.AuthorId).GreaterThan(0);
        RuleFor(c => c.Text)
            .NotEmpty()
            .MaximumLength(1000);
    }
}

public class CreateQuoteCommandHandler(AppDbContext db) : IRequestHandler<CreateQuoteCommand, int>
{
    public async Task<int> Handle(CreateQuoteCommand request, CancellationToken cancellationToken)
    {
        var authorExists = await db.Authors.AnyAsync(a => a.AuthorId == request.AuthorId, cancellationToken);
        if (!authorExists)
        {
            throw new InvalidOperationException($"Author {request.AuthorId} does not exist.");
        }

        var quote = new Quote
        {
            AuthorId = request.AuthorId,
            Text = request.Text,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Quotes.Add(quote);
        await db.SaveChangesAsync(cancellationToken);

        return quote.QuoteId;
    }
}
