using System.IO;
using System.Text.Json;
using Blockchain.Crypto;
using Blockchain.Models;

namespace Blockchain.Core;

public class Blockchain
{
    public List<Block> Chain { get; } = [];
    private string? _filePath;

    public Blockchain()
    {
        var genesis = new Block
        {
            Index = 0,
            PreviousHash = "0",
            Timestamp = 1288911600,
            Transactions = []
        };

        genesis.Hash = BlockHasher.Calculate(genesis);
        Chain.Add(genesis);
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
        Save();
    }
}