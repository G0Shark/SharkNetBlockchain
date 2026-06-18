using Blockchain.Models;

namespace Blockchain.Core;

public class Mempool
{
    private readonly List<Transaction> _txs = [];
    private readonly object _lock = new();

    public void Add(Transaction tx)
    {
        lock (_lock)
        {
            if (_txs.Any(t => t.Id == tx.Id))
                return;
            _txs.Add(tx);
        }
    }

    public List<Transaction> Take(int max)
    {
        lock (_lock)
        {
            var result = _txs.OrderByDescending(t => t.Amount * 0.1m).Take(max).ToList();
            _txs.RemoveAll(t => result.Contains(t));
            return result;
        }
    }
    
    public void Remove(List<Transaction> txs)
    {
        lock (_lock)
        {
            var idsToRemove = txs.Select(t => t.Id).ToHashSet();
            _txs.RemoveAll(t => idsToRemove.Contains(t.Id));
        }
    }
}