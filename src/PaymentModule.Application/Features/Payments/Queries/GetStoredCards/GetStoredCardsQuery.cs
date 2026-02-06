using MediatR;

namespace PaymentModule.Application.Features.Payments.Queries.GetStoredCards;

public record GetStoredCardsQuery(Guid UserId) : IRequest<List<StoredCardDto>>;
