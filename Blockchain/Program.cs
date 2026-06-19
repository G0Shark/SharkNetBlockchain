using System.IO;
using System.Net.Http;
using System.Text.Json;
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
   _____ _                _    _   _      _      _____ _      _____ 
  / ____| |              | |  | \ | |    | |    / ____| |    |_   _|
 | (___ | |__   __ _ _ __| | _|  \| | ___| |_  | |    | |      | |  
  \___ \| '_ \ / _` | '__| |/ / . ` |/ _ \ __| | |    | |      | |  
  ____) | | | | (_| | |  |   <| |\  |  __/ |_  | |____| |____ _| |_ 
 |_____/|_| |_|\__,_|_|  |_|\_\_| \_|\___|\__|  \_____|______|_____|

", ConsoleColor.Magenta);
Print("==================================================", ConsoleColor.DarkMagenta);
var port = args.Length > 0 ? args[0] : "5001";
// По умолчанию подключаемся к твоему домену
var bootstrapHost = args.Length > 1 ? args[1] : "sharknet.g0shark.ru"; 
var myHost = args.Length > 2 ? args[2] : "localhost";

var myUrl = $"ws://{myHost}:{port}";
var bootstrapApiUrl = $"http://{bootstrapHost}:7000";

Print($"Initializing node on {myUrl}...", ConsoleColor.Cyan);
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
    await http.PostAsync(
        $"{bootstrapApiUrl}/register",
        new StringContent(myUrl));
    Print("Registered on bootstrap server.", ConsoleColor.DarkGreen);

    // Получаем пиров
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

State ComputeState() => chain.CurrentState;

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

        var state = ComputeState();
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
Print("║                 YOUR WALLET INFO                 ║", ConsoleColor.Cyan);
Print("╠══════════════════════════════════════════════════╣", ConsoleColor.Cyan);
Print($"║ Nickname:  {myWallet.Nickname.PadRight(38)}║", ConsoleColor.White);
var shortKey = myWallet.PublicKey.Length > 30 
    ? myWallet.PublicKey[..15] + "..." + myWallet.PublicKey[^15..] 
    : myWallet.PublicKey;
Print($"║ Pub Key:   {shortKey.PadRight(38)}║", ConsoleColor.DarkGray);
Print("╚══════════════════════════════════════════════════╝\n", ConsoleColor.Cyan);

Print("Available commands: wallet, balances, peers, chain, tx <to> <amount> [message], mine, minegpu, exit\n", ConsoleColor.DarkCyan);

while (true)
{
    Print($"{port}> ", ConsoleColor.Green, false);
    var input = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(input)) 
        continue;

    if (input.ToLower() == "exit")
        break;

    var parts = input.Split(' ');
    var command = parts[0].ToLower();

    if (command == "wallet")
    {
        var state = ComputeState();
        var balance = state.Balances.TryGetValue(myWallet.Nickname, out var bal) ? bal : 0;
        Print($"Nickname: {myWallet.Nickname}", ConsoleColor.White);
        Print($"Balance:  {balance} coins", ConsoleColor.Yellow);
    }
    else if (command == "balances")
    {
        var state = ComputeState();
        Print("--- Account Balances ---", ConsoleColor.Cyan);
        if (state.Balances.Count == 0)
        {
            Print("No balances found.", ConsoleColor.DarkGray);
        }
        else
        {
            foreach (var kp in state.Balances)
            {
                var shortAddr = kp.Key.Length > 20 ? kp.Key[..10] + "..." + kp.Key[^10..] : kp.Key;
                var color = kp.Key == myWallet.Nickname ? ConsoleColor.Green : ConsoleColor.White;
                Print($"{shortAddr}: {kp.Value} coins", color);
            }
        }
    }
    else if (command == "peers")
    {
        var activePeers = node.GetConnectedPeers();
        Print("--- Connected Peers ---", ConsoleColor.Cyan);
        if (activePeers.Count == 0)
            Print("No active peers connected.", ConsoleColor.DarkGray);
        else
            activePeers.ForEach(p => Print(p, ConsoleColor.Yellow));
    }
    else if (command == "chain")
    {
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var chainJson = JsonSerializer.Serialize(chain.Chain, jsonOptions);
        Print(chainJson, ConsoleColor.DarkGray);
    }
    else if (command == "tx")
    {
        if (parts.Length < 3)
        {
            Print("Usage: tx <recipient_address> <amount> [optional_message]", ConsoleColor.Red);
            continue;
        }

        var recipient = parts[1];
        if (!decimal.TryParse(parts[2], out var amount) || amount <= 0)
        {
            Print("Invalid amount.", ConsoleColor.Red);
            continue;
        }

        var message = parts.Length > 3 ? string.Join(" ", parts[3..]) : "";

        decimal fee = amount * 0.1m;

        var state = ComputeState();
        var balance = state.Balances.TryGetValue(myWallet.Nickname, out var bal) ? bal : 0;
     
        if (balance < amount + fee)
        {
            Print($"Insufficient balance! You need {amount + fee} coins (transfer: {amount}, fee: {fee}).", ConsoleColor.Red);
            continue;
        }

        long nonce = state.Nonces.TryGetValue(myWallet.Nickname, out var n) ? n + 1 : 1;

        var tx = TransactionService.Create(
            myWallet.Nickname, 
            myWallet.PublicKey, 
            recipient, 
            amount,
            message, 
            nonce,
            myWallet.PrivateKey);
         
        mempool.Add(tx);
     
        await node.BroadcastTransaction(tx);
        Print($"Transaction {tx.Id} sent! Fee: {fee} coins.", ConsoleColor.Green);
    }
    else if (command == "mine")
    {
        var txs = mempool.Take(10);

        decimal totalFees = txs.Sum(t => t.Amount * 0.1m);
        decimal minerReward = 10 + totalFees;

        var coinbaseTx = TransactionService.CreateCoinbase(myWallet.Nickname, minerReward, myWallet.PublicKey);
        txs.Insert(0, coinbaseTx);

        var nextBlock = BlockService.CreateNextBlock(chain, txs);
     
        Print("Mining block...", ConsoleColor.Cyan, false);
        var miner = new Miner();
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        var miningTask = Task.Run(() => miner.Mine(nextBlock));
        
        char[] spinner = new char[] { '|', '/', '-', '\\' };
        int counter = 0;
        
        while (!miningTask.IsCompleted)
        {
            Console.Write($"\rMining block... [ {spinner[counter % 4]} ]");
            counter++;
            await Task.Delay(150);
        }
        
        var minedBlock = await miningTask;
        
        Console.Write("\r                                                   \r");

        chain.Add(minedBlock);
        await node.BroadcastBlock(minedBlock);

        Print($"\nBlock #{minedBlock.Index} successfully mined in {stopwatch.Elapsed.TotalSeconds:F2}s!", ConsoleColor.Green);
        Print($"Hash:  {minedBlock.Hash}", ConsoleColor.Yellow);
        Print($"Nonce: {minedBlock.Nonce}", ConsoleColor.White);
        Print($"Transactions: {minedBlock.Transactions.Count}", ConsoleColor.White);
    }
    else if (command == "minegpu")
    {
        var txs = mempool.Take(10);

        decimal totalFees = txs.Sum(t => t.Amount * 0.1m);
        decimal minerReward = 10 + totalFees;

        var coinbaseTx = TransactionService.CreateCoinbase(myWallet.Nickname, minerReward, myWallet.PublicKey);
        txs.Insert(0, coinbaseTx);

        var nextBlock = BlockService.CreateNextBlock(chain, txs);
     
        Print("Mining block on GPU...", ConsoleColor.Cyan, false);
        using var cts = new CancellationTokenSource();
        var miner = new GPUMiner();
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        var miningTask = Task.Run(() => miner.Mine(nextBlock, cts.Token));
        
        char[] spinner = new char[] { '|', '/', '-', '\\' };
        int counter = 0;
        
        while (!miningTask.IsCompleted)
        {
            Console.Write($"\rMining block on GPU... [ {spinner[counter % 4]} ]");
            counter++;
            await Task.Delay(150);
        }
        
        var minedBlock = await miningTask;
        
        Console.Write("\r                                                   \r");

        chain.Add(minedBlock);
        await node.BroadcastBlock(minedBlock);

        Print($"\nBlock #{minedBlock.Index} successfully mined on GPU in {stopwatch.Elapsed.TotalSeconds:F2}s!", ConsoleColor.Green);
        Print($"Hash:  {minedBlock.Hash}", ConsoleColor.Yellow);
        Print($"Nonce: {minedBlock.Nonce}", ConsoleColor.White);
        Print($"Transactions: {minedBlock.Transactions.Count}", ConsoleColor.White);
    }
    else
    {
        Print("Unknown command. Supported: wallet, balances, peers, chain, tx, mine, minegpu, exit", ConsoleColor.Red);
    }
}