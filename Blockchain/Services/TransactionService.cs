using Blockchain.Models;
using Blockchain.Network;

namespace Blockchain.Services;

public static class TransactionService
{
    public static Transaction Create(
        string from,
        string pubKey,
        string to,
        decimal amount,
        string message,
        string privateKey)
    {
        var msg = message.Length > 128 ? message[..128] : message;
        
        var tx = new Transaction
        {
            From = from,
            PublicKey = pubKey,
            To = to,
            Amount = amount,
            Message = msg,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        tx.Id = CalculateId(tx);

        tx.Signature = SignatureService.Sign(
            tx.Id,
            privateKey);

        return tx;
    }
    
    public static Transaction CreateCoinbase(string to, decimal reward, string pubKey)
    {
        var tx = new Transaction
        {
            From = "coinbase",
            PublicKey = pubKey,
            To = to,
            Amount = reward,
            Message = "Mining block award, thanks for helping",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Signature = ""
        };

        tx.Id = CalculateId(tx);
        return tx;
    }

    public static bool Validate(Transaction tx)
    {
        var expectedId = CalculateId(tx);

        if (expectedId != tx.Id)
            return false;

        if (tx.From == "coinbase")
            return true;

        return SignatureService.Verify(
            tx.Id,
            tx.Signature,
            tx.PublicKey);
    }
    
    public static string CalculateId(Transaction tx)
    {
        return HashService.Sha256(
            $"{tx.From}:{tx.PublicKey}:{tx.To}:{tx.Amount}:{tx.Message}:{tx.Timestamp}");
    }
}