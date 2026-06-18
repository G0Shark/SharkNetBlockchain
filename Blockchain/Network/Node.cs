using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Blockchain.Core;
using Blockchain.Crypto;
using Blockchain.Models;
using Blockchain.Services;

namespace Blockchain.Network;

public class Node
{
    private readonly HttpListener _server = new();
    private readonly List<Peer> _peers = [];
    public string? Url { get; private set; }

    public Core.Blockchain Blockchain { get; }
    public Mempool Mempool { get; }

    public Node(Core.Blockchain chain, Mempool mempool)
    {
        Blockchain = chain;
        Mempool = mempool;
    }

    public List<string> GetConnectedPeers()
    {
        return _peers.Select(p => p.Url).ToList();
    }
    
    public void Start(string url, string? listenPrefix = null)
    {
        Url = url;
        var httpUrl = (listenPrefix ?? url).Replace("ws://", "http://");
        if (!httpUrl.EndsWith("/")) 
            httpUrl += "/";

        _server.Prefixes.Add(httpUrl);
        _server.Start();

        _ = Task.Run(Listen);
    }

    private async Task Listen()
    {
        while (true)
        {
            var context = await _server.GetContextAsync();

            if (!context.Request.IsWebSocketRequest)
                continue;

            var wsContext = await context.AcceptWebSocketAsync(null);
            _ = Task.Run(() => Handle(wsContext.WebSocket));
        }
    }

    private async Task Handle(WebSocket socket)
    {
        var buffer = new byte[8192];

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(
                    buffer,
                    CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }

                var json = Encoding.UTF8.GetString(
                    buffer, 0, result.Count);

                var msg = JsonSerializer.Deserialize<Message>(json);

                if (msg == null) continue;

                await ProcessMessage(msg, socket);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection error: {ex.Message}");
        }
        finally
        {
            var peerToRemove = _peers.FirstOrDefault(p => p.Socket == socket);
            if (peerToRemove != null)
            {
                _peers.Remove(peerToRemove);
                Console.WriteLine($"Removed peer: {peerToRemove.Url}");
            }
            socket.Dispose();
        }
    }
    
    private async Task ProcessMessage(Message msg, WebSocket socket)
    {
        switch (msg.Type)
        {
            case MessageType.Transaction:
                var tx = JsonSerializer.Deserialize<Transaction>(msg.Data);
                if (tx != null)
                {
                    if (!TransactionService.Validate(tx))
                    {
                        Console.WriteLine($"[Network] Transaction {tx.Id} has invalid signature. Rejected.");
                        break;
                    }

                    var state = new State();
                    foreach (var b in Blockchain.Chain)
                    {
                        foreach (var existingTx in b.Transactions)
                        {
                            StateService.Apply(state, existingTx);
                        }
                    }

                    if (state.NicknameKeys.TryGetValue(tx.From, out var registeredKey) && registeredKey != tx.PublicKey)
                    {
                        Console.WriteLine($"[Network] Nickname {tx.From} belongs to another public key. Rejected.");
                        break;
                    }

                    var balance = state.Balances.TryGetValue(tx.From, out var bal) ? bal : 0;
                    if (balance < tx.Amount)
                    {
                        Console.WriteLine($"[Network] Sender {tx.From} has insufficient balance. Rejected.");
                        break;
                    }

                    Mempool.Add(tx);
                    Console.WriteLine($"[Network] Valid transaction {tx.Id} added to mempool.");
                }
                break;

            case MessageType.Block:
                var block = JsonSerializer.Deserialize<Block>(msg.Data);

                if (block != null &&
                    ValidateBlock(block))
                {
                    Blockchain.Add(block);
                    Mempool.Remove(block.Transactions);
                }
                else
                {
                    Console.WriteLine($"Block #{block.Index} is rejected for invalid");
                    await Send(socket, new Message
                    {
                        Type = MessageType.GetChain,
                        Data = ""
                    });
                }
                break;

            case MessageType.GetChain:
                var chainJson = JsonSerializer.Serialize(Blockchain.Chain);

                await Send(socket, new Message
                {
                    Type = MessageType.Chain,
                    Data = chainJson
                });
                break;
            
            case MessageType.Chain:
                var incomingChain = JsonSerializer.Deserialize<List<Block>>(msg.Data);
                if (incomingChain != null && incomingChain.Count > Blockchain.Chain.Count)
                {
                    Console.WriteLine($"\n[Consensus] Received a longer chain from peer (length: {incomingChain.Count}). Validating...");
                    if (ValidateChain(incomingChain))
                    {
                        Blockchain.Chain.Clear();
                        Blockchain.Chain.AddRange(incomingChain);
                        Blockchain.Save();
                        
                        foreach (var b in incomingChain)
                        {
                            Mempool.Remove(b.Transactions);
                        }

                        Console.WriteLine("[Consensus] Our chain was replaced with the longer valid chain!");
                    }
                    else
                    {
                        Console.WriteLine("[Consensus] Received chain is invalid. Replaced rejected.");
                    }
                }
                break;
            
            case MessageType.Hello:
                var senderUrl = msg.Data;
                if (!string.IsNullOrEmpty(senderUrl) && !_peers.Any(p => p.Url == senderUrl))
                {
                    _peers.Add(new Peer
                    {
                        Url = senderUrl,
                        Socket = socket
                    });
                    Console.WriteLine($"Registered incoming peer: {senderUrl}");
                }
                break;
        }
    }
    
    private async Task Send(WebSocket socket, Message msg)
    {
        var json = JsonSerializer.Serialize(msg);

        var bytes = Encoding.UTF8.GetBytes(json);

        await socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }
    
    public async Task Connect(string url)
    {
        var peer = new ClientWebSocket();

        await peer.ConnectAsync(
            new Uri(url),
            CancellationToken.None);

        _peers.Add(new Peer
        {
            Url = url,
            Socket = peer
        });

        _ = Task.Run(() => Handle(peer));

        await Send(peer, new Message
        {
            Type = MessageType.Hello,
            Data = Url ?? ""
        });
        
        await Send(peer, new Message
        {
            Type = MessageType.GetChain,
            Data = ""
        });
    }
    
    public async Task BroadcastTransaction(Transaction tx)
    {
        var msg = new Message
        {
            Type = MessageType.Transaction,
            Data = JsonSerializer.Serialize(tx)
        };

        foreach (var peer in _peers)
        {
            if (peer.Socket != null)
            {
                await Send(peer.Socket, msg);
            }
        }
    }

    public async Task BroadcastBlock(Block block)
    {
        var msg = new Message
        {
            Type = MessageType.Block,
            Data = JsonSerializer.Serialize(block)
        };

        foreach (var peer in _peers)
        {
            if (peer.Socket != null)
            {
                await Send(peer.Socket, msg);
            }
        }
    }
    
    private bool ValidateBlock(Block block)
    {
        var hash = BlockHasher.Calculate(block);
        if (hash != block.Hash)
            return false;

        if (!block.Hash.StartsWith("00000000"))
            return false;

        if (block.PreviousHash != Blockchain.Last().Hash)
            return false;

        var state = new State();
        foreach (var b in Blockchain.Chain)
        {
            foreach (var existingTx in b.Transactions)
            {
                StateService.Apply(state, existingTx);
            }
        }

        var hasCoinbase = false;
        foreach (var tx in block.Transactions)
        {
            if (!TransactionService.Validate(tx))
                return false;

            if (tx.From == "coinbase")
            {
                if (hasCoinbase) return false;
                hasCoinbase = true;

                decimal totalFees = block.Transactions
                    .Where(t => t.From != "coinbase")
                    .Sum(t => t.Amount * 0.1m);

                if (tx.Amount != 10 + totalFees)
                    return false;
            }

            if (!StateService.Apply(state, tx))
                return false;
        }

        return true;
    }
    
    private bool ValidateChain(List<Block> newChain)
    {
        if (newChain.Count == 0 || newChain[0].Hash != Blockchain.Chain[0].Hash)
            return false;

        var tempState = new State();

        foreach (var tx in newChain[0].Transactions)
        {
            if (!StateService.Apply(tempState, tx))
                return false;
        }

        for (int i = 1; i < newChain.Count; i++)
        {
            var current = newChain[i];
            var prev = newChain[i - 1];

            if (current.PreviousHash != prev.Hash)
                return false;

            if (BlockHasher.Calculate(current) != current.Hash)
                return false;

            if (!current.Hash.StartsWith("00000000"))
                return false;

            var hasCoinbase = false;
            foreach (var tx in current.Transactions)
            {
                if (!TransactionService.Validate(tx))
                    return false;

                if (tx.From == "coinbase")
                {
                    if (hasCoinbase) return false;
                    hasCoinbase = true;
                    if (tx.Amount != 10) return false;
                }

                if (!StateService.Apply(tempState, tx))
                    return false;
            }
        }

        return true;
    }
}