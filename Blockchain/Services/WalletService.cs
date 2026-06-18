using System.Security.Cryptography;
using Blockchain.Models;

namespace Blockchain.Services;

public class WalletService
{
    public static Wallet CreateWallet(string nick)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        return new Wallet()
        {
            PublicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()),
            PrivateKey = Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()),
            Nickname = nick
        };
    }
}