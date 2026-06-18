namespace Blockchain.Network;

public class Message
{
    public MessageType Type { get; set; }

    public string Data { get; set; } = "";
}