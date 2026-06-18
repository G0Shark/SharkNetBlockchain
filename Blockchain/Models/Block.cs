namespace Blockchain.Models;

public class Block
{
    public int Index { get; set; }

    public string PreviousHash { get; set; } = "";

    public long Timestamp { get; set; }

    public List<Transaction> Transactions { get; set; } = [];

    public long Nonce { get; set; }

    public int Difficulty { get; set; } = 8;

    public string Hash { get; set; } = "";
    
    public Block Clone()
    {
        return new Block
        {
            Index = Index,
            PreviousHash = PreviousHash,
            Timestamp = Timestamp,
            Nonce = Nonce,
            Difficulty = Difficulty,
            Hash = Hash,

            // если транзакции во время майнинга не меняются
            Transactions = new List<Transaction>(Transactions)
        };
    }
}