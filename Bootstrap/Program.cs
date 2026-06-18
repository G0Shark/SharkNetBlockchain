using System.IO;
using System.Net;
using System.Text;
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
   _____ _                _    _   _      _     ____              _       _                             
  / ____| |              | |  | \ | |    | |   |  _ \            | |     | |                            
 | (___ | |__   __ _ _ __| | _|  \| | ___| |_  | |_) | ___   ___ | |_ ___| |_ _ __ __ _ _ __   ___ _ __ 
  \___ \| '_ \ / _` | '__| |/ / . ` |/ _ \ __| |  _ < / _ \ / _ \| __/ __| __| '__/ _` | '_ \ / _ \ '__|
  ____) | | | | (_| | |  |   <| |\  |  __/ |_  | |_) | (_) | (_) | |_\__ \ |_| | | (_| | |_) |  __/ |   
 |_____/|_| |_|\__,_|_|  |_|\_\_| \_|\___|\__| |____/ \___/ \___/ \__|___/\__|_|  \__,_| .__/ \___|_|   
                                                                                       | |              
                                                                                       |_|              

", ConsoleColor.Magenta);
Print("==================================================", ConsoleColor.DarkMagenta);

// По умолчанию хостом на сервере будет твой домен
var host = args.Length > 0 ? args[0] : "sharknet.g0shark.ru";
var bootstrapUrl = host == "localhost" ? "http://localhost:7000/" : "http://*:7000/";
var seedNodeP2PUrl = $"ws://{host}:7001";

Console.WriteLine($"Starting Bootstrap on port 7000...");
Console.WriteLine($"Seed node will advertise as {seedNodeP2PUrl}");

var chain = new Blockchain.Core.Blockchain();
var mempool = new Mempool();
var node = new Node(chain, mempool);

chain.InitializePersistence("7001");

var listenPrefix = host == "localhost" ? "ws://localhost:7001" : "ws://*:7001";
try
{
    node.Start(seedNodeP2PUrl, listenPrefix);
    Console.WriteLine("Seed Node P2P Server started.");
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to start Seed Node P2P: {ex.Message}");
    return;
}

Wallet seedWallet;
var walletPath = "wallet_7001.json";
if (File.Exists(walletPath))
{
    var json = File.ReadAllText(walletPath);
    seedWallet = JsonSerializer.Deserialize<Wallet>(json) ?? WalletService.CreateWallet("seed_miner");
}
else
{
    seedWallet = WalletService.CreateWallet("seed_miner");
    var json = JsonSerializer.Serialize(seedWallet, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(walletPath, json);
}
Console.WriteLine($"Seed Node Wallet: {seedWallet.Nickname}");

var peers = new List<string> { seedNodeP2PUrl };

_ = Task.Run(async () =>
{
    var miner = new Miner();
    while (true)
    {
        await Task.Delay(5000);

        var txs = mempool.Take(10);
        if (txs.Count > 0)
        {
            Console.WriteLine($"\n[Seed Miner] Found {txs.Count} transactions in mempool. Mining started...");
            
            decimal totalFees = txs.Sum(t => t.Amount * 0.1m);
            decimal reward = 10 + totalFees;

            var coinbaseTx = TransactionService.CreateCoinbase(seedWallet.Nickname, reward, seedWallet.PublicKey);
            txs.Insert(0, coinbaseTx);

            var nextBlock = BlockService.CreateNextBlock(chain, txs);
            var minedBlock = miner.Mine(nextBlock);

            chain.Add(minedBlock);
            await node.BroadcastBlock(minedBlock);

            Console.WriteLine($"[Seed Miner] Block #{minedBlock.Index} successfully mined. Reward: {reward} coins.");
        }
    }
});

var listener = new HttpListener();
listener.Prefixes.Add(bootstrapUrl);
listener.Start();
Console.WriteLine("Bootstrap Server is running on port 7000.");

while (true)
{
    var ctx = await listener.GetContextAsync();
    var path = ctx.Request.Url!.AbsolutePath;

    if (path == "/register" && ctx.Request.HttpMethod == "POST")
    {
        using var reader = new StreamReader(ctx.Request.InputStream);
        var url = await reader.ReadToEndAsync();

        if (!peers.Contains(url) && !string.IsNullOrEmpty(url))
        {
            peers.Add(url);
            Console.WriteLine($"New peer registered: {url}");
        }

        var responseBytes = Encoding.UTF8.GetBytes("OK");
        await ctx.Response.OutputStream.WriteAsync(responseBytes);
        ctx.Response.Close();
    }
    else if (path == "/peers" && ctx.Request.HttpMethod == "GET")
    {
        var json = JsonSerializer.Serialize(peers);
        var buffer = Encoding.UTF8.GetBytes(json);

        ctx.Response.ContentType = "application/json";
        await ctx.Response.OutputStream.WriteAsync(buffer);
        ctx.Response.Close();
    }
}