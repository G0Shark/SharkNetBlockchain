using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Blockchain.Core;
using Blockchain.Models;
using Blockchain.Network;
using Blockchain.Services;

void Print(string text, ConsoleColor color = ConsoleColor.Gray, bool writeLine = true)
{
    var originalColor = Console.ForegroundColor;
    Console.ForegroundColor = color;
    if (writeLine) Console.WriteLine(text);
    else Console.Write(text);
    Console.ForegroundColor = originalColor;
}

Console.Clear();
Print(@"
   _____ _                _    _   _      _     __  __ _                 
  / ____| |              | |  | \ | |    | |   |  \/  (_)                
 | (___ | |__   __ _ _ __| | _|  \| | ___| |_  | \  / |_ _ __   ___ _ __ 
  \___ \| '_ \ / _` | '__| |/ / . ` |/ _ \ __| | |\/| | | '_ \ / _ \ '__|
  ____) | | | | (_| | |  |   <| |\  |  __/ |_  | |  | | | | | |  __/ |   
 |_____/|_| |_|\__,_|_|  |_|\_\_| \_|\___|\__| |_|  |_|_|_| |_|\___|_|   
                                                                          
", ConsoleColor.Magenta);
Print("==================================================", ConsoleColor.DarkMagenta);

var port = args.Length > 0 ? args[0] : "5003";
var bootstrapHost = args.Length > 1 ? args[1] : "sharknet.g0shark.ru";
var myHost = args.Length > 2 ? args[2] : "localhost";

var myUrl = $"ws://{myHost}:{port}";
var bootstrapApiUrl = $"http://{bootstrapHost}:7000";

Print($"Initializing miner node on {myUrl}...", ConsoleColor.Cyan);
Print($"Connecting to Bootstrap at {bootstrapApiUrl}...", ConsoleColor.Cyan);

var chain = new Blockchain.Core.Blockchain();
var mempool = new Mempool();
var node = new Node(chain, mempool);
chain.InitializePersistence(port);

var listenPrefix = myHost == "localhost" ? $"ws://localhost:{port}" : $"ws://*:{port}";
try
{
    node.Start(myUrl, listenPrefix);
    Print("P2P Server started listening.", ConsoleColor.DarkGreen);
}
catch (Exception ex)
{
    Print($"Failed to start P2P server: {ex.Message}", ConsoleColor.Red);
    return;
}

var http = new HttpClient();
try
{
    await http.PostAsync($"{bootstrapApiUrl}/register", new StringContent(myUrl));
    Print("Registered on bootstrap server.", ConsoleColor.DarkGreen);

    var json = await http.GetStringAsync($"{bootstrapApiUrl}/peers");
    var peers = JsonSerializer.Deserialize<List<string>>(json) ?? new();
    Print($"Found {peers.Count} peers on network. Syncing...", ConsoleColor.Yellow);

    foreach (var peer in peers)
    {
        if (peer == myUrl)
            continue;

        try
        {
            await node.Connect(peer);
            Print($"Successfully connected to peer: {peer}", ConsoleColor.Green);
        }
        catch (Exception ex)
        {
            Print($"Failed to connect to peer {peer}: {ex.Message}", ConsoleColor.Red);
        }
    }
}
catch (Exception ex)
{
    Print($"Bootstrap server not available ({ex.Message}). Running in standalone mode.", ConsoleColor.Yellow);
}

Wallet myWallet;
var walletPath = $"wallet_{port}.json";

if (File.Exists(walletPath))
{
    try
    {
        var walletJson = File.ReadAllText(walletPath);
        myWallet = JsonSerializer.Deserialize<Wallet>(walletJson) 
                   ?? throw new Exception("Invalid wallet file format");
        Print($"\nWelcome back, {myWallet.Nickname}!", ConsoleColor.Green);
    }
    catch (Exception ex)
    {
        Print($"Error loading wallet: {ex.Message}. Creating a new one.", ConsoleColor.Red);
        myWallet = CreateNewWalletInteractive();
    }
}
else
{
    myWallet = CreateNewWalletInteractive();
}

Wallet CreateNewWalletInteractive()
{
    Print("\n--- Wallet Creation ---", ConsoleColor.Cyan);
    while (true)
    {
        Print("Enter nickname for new wallet: ", ConsoleColor.White, false);
        var nick = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(nick) || nick.ToLower() == "coinbase" || nick.Contains(' '))
        {
            Print("Invalid nickname. Try another one.", ConsoleColor.Red);
            continue;
        }

        var state = chain.CurrentState;
        if (state.NicknameKeys.ContainsKey(nick))
        {
            Print($"Nickname '{nick}' is already taken on the blockchain! Try another one.", ConsoleColor.Red);
            continue;
        }

        var wallet = WalletService.CreateWallet(nick);
        var json = JsonSerializer.Serialize(wallet, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(walletPath, json);
        Print($"Wallet saved to {walletPath}", ConsoleColor.Green);
        
        return wallet;
    }
}

Print("\n╔══════════════════════════════════════════════════╗", ConsoleColor.Cyan);
Print("║                 MINER NODE ACTIVE                ║", ConsoleColor.Cyan);
Print("╠══════════════════════════════════════════════════╣", ConsoleColor.Cyan);
Print($"║ Nickname:  {myWallet.Nickname.PadRight(38)}║", ConsoleColor.White);
var shortKey = myWallet.PublicKey.Length > 30 
    ? myWallet.PublicKey[..15] + "..." + myWallet.PublicKey[^15..] 
    : myWallet.PublicKey;
Print($"║ Pub Key:   {shortKey.PadRight(38)}║", ConsoleColor.DarkGray);
Print("╚══════════════════════════════════════════════════╝\n", ConsoleColor.Cyan);

Print("Starting infinite mining loop...", ConsoleColor.DarkCyan);

while (true)
{
    var txs = mempool.Take(10);
    decimal totalFees = txs.Sum(t => t.Amount * 0.1m);
    decimal minerReward = 10 + totalFees;

    var coinbaseTx = TransactionService.CreateCoinbase(myWallet.Nickname, minerReward, myWallet.PublicKey);
    txs.Insert(0, coinbaseTx);

    var nextBlock = BlockService.CreateNextBlock(chain, txs);
    Print($"[{DateTime.Now:HH:mm:ss}] Mining block #{nextBlock.Index} (difficulty: {nextBlock.Difficulty}, txs: {txs.Count})...", ConsoleColor.Yellow);

    var miner = new Blockchain.Core.Miner();
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    var minedBlock = await Task.Run(() => miner.Mine(nextBlock));

    if (minedBlock.PreviousHash == chain.Last().Hash)
    {
        chain.Add(minedBlock);
        await node.BroadcastBlock(minedBlock);
        Print($"[{DateTime.Now:HH:mm:ss}] Successfully mined block #{minedBlock.Index} in {stopwatch.Elapsed.TotalSeconds:F2}s!", ConsoleColor.Green);
        Print($"Hash:  {minedBlock.Hash}", ConsoleColor.Cyan);
    }
    else
    {
        Print($"[{DateTime.Now:HH:mm:ss}] Block #{minedBlock.Index} obsolete. Discarding.", ConsoleColor.DarkYellow);
        txs.RemoveAt(0);
        foreach (var tx in txs)
        {
            mempool.Add(tx);
        }
    }

    await Task.Delay(1000);
}