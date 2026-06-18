using Blockchain.Models;
using Blockchain.Services;

namespace Blockchain.Crypto;

public static class BlockHasher
{
    public static string Calculate(Block block)
    {
        var txData = string.Join("|",
            block.Transactions.Select(t => t.Id));

        var raw =
            $"{block.Index}" +
            $"{block.PreviousHash}" +
            $"{block.Timestamp}" +
            $"{block.Nonce}" +
            $"{txData}";

        return HashService.Sha256(raw);
    }
}