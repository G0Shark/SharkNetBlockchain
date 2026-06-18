using System.IO;
using System.Text.Json;
using Blockchain.Crypto;
using Blockchain.Models;
using Blockchain.Services;

namespace Blockchain.Core;

public class Blockchain
{
    public List<Block> Chain { get; } = [];
    public State CurrentState { get; } = new();
    public object StateLock { get; } = new();
    private string? _filePath;

    public Blockchain()
    {
        var genesis = new Block
        {
            Index = 0,
            PreviousHash = "0",
            Timestamp = 1288911600,
            Transactions = [],
            Difficulty = 8
        };

        genesis.Hash = BlockHasher.Calculate(genesis);
        Chain.Add(genesis);
        StateService.Apply(CurrentState, genesis.Transactions.FirstOrDefault() ?? new Transaction());
    }

    public void InitializePersistence(string port)
    {
        _filePath = $"chain_{port}.json";
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<List<Block>>(json);
                if (loaded != null && loaded.Count > 0)
                {
                    Chain.Clear();
                    Chain.AddRange(loaded);
                    Console.WriteLine($"Loaded {loaded.Count} blocks from database.");
                    RebuildState();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading blockchain database: {ex.Message}");
            }
        }
        else
        {
            Save();
        }
    }

    public void RebuildState()
    {
        lock (StateLock)
        {
            CurrentState.Balances.Clear();
            CurrentState.NicknameKeys.Clear();
            CurrentState.Nonces.Clear();
            foreach (var block in Chain)
            {
                foreach (var tx in block.Transactions)
                {
                    StateService.Apply(CurrentState, tx);
                }
            }
        }
    }

    public int GetNextDifficulty()
    {
        return GetDifficultyForIndex(Chain, Chain.Count);
    }

    public static int GetDifficultyForIndex(List<Block> chain, int index)
    {
        if (index <= 0) return 8;

        if (index % 5 == 0 && index >= 5)
        {
            var prevBlock = chain[index - 1];
            var prev5Block = chain[index - 5];
            var timeSpan = prevBlock.Timestamp - prev5Block.Timestamp;

            if (timeSpan < 6000)
                return prevBlock.Difficulty + 1;
            if (timeSpan > 12000)
                return Math.Max(1, prevBlock.Difficulty - 1);

            return prevBlock.Difficulty;
        }

        return chain[index - 1].Difficulty;
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(_filePath)) return;
        try
        {
            var json = JsonSerializer.Serialize(Chain, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving blockchain database: {ex.Message}");
        }
    }

    public Block Last() => Chain.Last();

    public void Add(Block block)
    {
        Chain.Add(block);
        lock (StateLock)
        {
            foreach (var tx in block.Transactions)
            {
                StateService.Apply(CurrentState, tx);
            }
        }
        Save();
    }
}