using Blockchain.Models;

namespace Blockchain.Services;

public static class StateService
{
    public static bool Apply(State state, Transaction tx)
    {
        if (tx.From == "coinbase")
        {
            if (state.NicknameKeys.TryGetValue(tx.To, out var registeredKey))
            {
                if (registeredKey != tx.PublicKey)
                    return false;
            }
            else
            {
                state.NicknameKeys[tx.To] = tx.PublicKey;
            }
        }
        else
        {
            if (state.NicknameKeys.TryGetValue(tx.From, out var registeredKey))
            {
                if (registeredKey != tx.PublicKey)
                    return false;
            }
            else
            {
                state.NicknameKeys[tx.From] = tx.PublicKey;
            }

            long currentNonce = state.Nonces.TryGetValue(tx.From, out var n) ? n : 0;
            if (tx.Nonce != currentNonce + 1)
                return false;

            state.Nonces[tx.From] = tx.Nonce;
        }

        if (tx.From == "coinbase")
        {
            if (!state.Balances.ContainsKey(tx.To))
                state.Balances[tx.To] = 0;

            state.Balances[tx.To] += tx.Amount;
            return true;
        }

        if (!state.Balances.ContainsKey(tx.From))
            state.Balances[tx.From] = 0;

        if (!state.Balances.ContainsKey(tx.To))
            state.Balances[tx.To] = 0;

        decimal fee = tx.Amount * 0.1m;

        if (state.Balances[tx.From] < tx.Amount + fee)
            return false;

        state.Balances[tx.From] -= (tx.Amount + fee);
        state.Balances[tx.To] += tx.Amount;

        return true;
    }
}