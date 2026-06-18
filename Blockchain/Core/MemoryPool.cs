using Blockchain.Models;

namespace Blockchain.Core;

public class Mempool
{
    private readonly List<Transaction> _txs = [];

    public void Add(Transaction tx)
    {
        _txs.Add(tx);
    }

    public List<Transaction> Take(int max)
    {
        var result = _txs.Take(max).ToList();

        _txs.RemoveAll(t => result.Contains(t));

        return result;
    }
    
    public void Remove(List<Transaction> txs)
    {
        var idsToRemove = txs.Select(t => t.Id).ToHashSet();
        _txs.RemoveAll(t => idsToRemove.Contains(t.Id));
    }
}