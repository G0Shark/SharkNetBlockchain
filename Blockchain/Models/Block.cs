namespace Blockchain.Models;

public class Block
{
    public int Index { get; set; }

    public string PreviousHash { get; set; } = "";

    public long Timestamp { get; set; }

    public List<Transaction> Transactions { get; set; } = [];

    public long Nonce { get; set; }

    public string Hash { get; set; } = "";
}