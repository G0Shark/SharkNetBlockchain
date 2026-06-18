using System.Net.WebSockets;

namespace Blockchain.Network;


public class Peer
{
    public string Id { get; set; } = "";

    public string Url { get; set; } = "";

    public WebSocket? Socket { get; set; }
}