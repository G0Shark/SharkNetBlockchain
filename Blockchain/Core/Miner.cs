using Blockchain.Crypto;
using Blockchain.Models;

namespace Blockchain.Core;

public class Miner
{
    public Block Mine(Block block)
    {
        string target = "000000";

        while (true)
        {
            block.Hash = BlockHasher.Calculate(block);

            if (block.Hash.StartsWith(target))
                return block;

            block.Nonce++;
        }
    }
}