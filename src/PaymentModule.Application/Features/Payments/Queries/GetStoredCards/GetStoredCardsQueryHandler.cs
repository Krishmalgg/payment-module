using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentModule.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace PaymentModule.Application.Features.Payments.Queries.GetStoredCards;

public class GetStoredCardsQueryHandler : IRequestHandler<GetStoredCardsQuery, List<StoredCardDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<GetStoredCardsQueryHandler> _logger;

    public GetStoredCardsQueryHandler(IApplicationDbContext dbContext, ILogger<GetStoredCardsQueryHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<StoredCardDto>> Handle(GetStoredCardsQuery request, CancellationToken cancellationToken)
    {
        // Use IgnoreQueryFilters to bypass RLS and allow S2S access to any user's cards
        _logger.LogInformation("GetStoredCards request for UserId: {UserId}", request.UserId);
        var cards = await _dbContext.StoredCards
            .IgnoreQueryFilters()
            .Where(x => x.UserId == request.UserId && x.Status == "ACTIVE")
            .Select(x => new StoredCardDto
            {
                Id = x.Id,
                CardHolderName = x.CardHolderName,
                CardNo = MaskCardNumber(x.CardNo), // Ensure we don't return full number if it was raw, but here it's decrypted
                CardExpiry = x.CardExpiry,
                Brand = x.CardType,
                Token = x.CustomerToken
            })
            .ToListAsync(cancellationToken);
        // Never log the DTOs themselves — they carry chargeable provider tokens.
        _logger.LogInformation("Fetched {Count} stored cards for UserId {UserId}", cards.Count, request.UserId);

        return cards;
    }

    private static string MaskCardNumber(string cardNo)
    {
        // If the provider already returned a masked number, leave it alone
        if (cardNo.Contains('*')) return cardNo;
        
        // Otherwise mask all but last 4
        if (string.IsNullOrEmpty(cardNo) || cardNo.Length < 4) return cardNo;
        
        return new string('*', cardNo.Length - 4) + cardNo[^4..];
    }
}
