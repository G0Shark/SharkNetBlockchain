namespace Blockchain.Models;

public class Transaction
{
    public string From { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string To { get; set; } = "";
    public decimal Amount { get; set; }
    public string Message { get; set; } = "";

    public long Timestamp { get; set; }
    public long Nonce { get; set; }

    public string Signature { get; set; } = "";

    public string Id { get; set; } = "";
}