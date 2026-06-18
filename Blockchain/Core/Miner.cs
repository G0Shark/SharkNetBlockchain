using Blockchain.Crypto;
using Blockchain.Models;

namespace Blockchain.Core;

public class Miner
{
    public Block Mine(Block block)
    {
        var target = new string('0', block.Difficulty);

        int workers = Environment.ProcessorCount;

        Block? result = null;

        using var cts = new CancellationTokenSource();

        Parallel.For((long)0, workers, workerId =>
        {
            var localBlock = block.Clone();

            long nonce = workerId;

            while (!cts.Token.IsCancellationRequested)
            {
                localBlock.Nonce = nonce;

                string hash = BlockHasher.Calculate(localBlock);

                if (hash.StartsWith(target))
                {
                    localBlock.Hash = hash;
                    result = localBlock;

                    cts.Cancel();
                    break;
                }

                nonce += workers;
            }
        });

        return result!;
    }
}