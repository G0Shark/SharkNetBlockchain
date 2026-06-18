namespace Blockchain.Models;

public class State
{
    public Dictionary<string, decimal> Balances { get; } = new();
    public Dictionary<string, string> NicknameKeys { get; } = new();
    public Dictionary<string, long> Nonces { get; } = new();

    public State Clone()
    {
        var copy = new State();
        foreach (var kp in Balances) copy.Balances[kp.Key] = kp.Value;
        foreach (var kp in NicknameKeys) copy.NicknameKeys[kp.Key] = kp.Value;
        foreach (var kp in Nonces) copy.Nonces[kp.Key] = kp.Value;
        return copy;
    }
}