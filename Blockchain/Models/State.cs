namespace Blockchain.Models;

public class State
{
    public Dictionary<string, decimal> Balances { get; } = new();
    public Dictionary<string, string> NicknameKeys { get; } = new();
}