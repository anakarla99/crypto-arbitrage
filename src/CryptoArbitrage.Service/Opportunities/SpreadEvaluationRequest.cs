namespace CryptoArbitrage.Service.Opportunities;

public sealed record SpreadEvaluationRequest(string BuyAskPrice, string BuyAskQuantity, string SellBidPrice, string SellBidQuantity);
