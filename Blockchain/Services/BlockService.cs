using Blockchain.Models;

namespace Blockchain.Services;

public static class BlockService
{
    public static Block CreateNextBlock(
        Core.Blockchain chain,
        List<Transaction> txs)
    {
        return new Block
        {
            Index = chain.Last().Index + 1,
            PreviousHash = chain.Last().Hash,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Transactions = txs
        };
    }
}