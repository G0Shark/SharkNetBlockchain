using System.Security.Cryptography;
using System.Text;

namespace Blockchain.Services;

public class SignatureService
{
    public static string Sign(string message, string privateKey)
    {
        using var ecdsa = ECDsa.Create();
        
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
        
        var sign = ecdsa.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256);
        
        return Convert.ToBase64String(sign);
    }
    
    public static bool Verify(
        string data,
        string signature,
        string publicKey)
    {
        using var ecdsa = ECDsa.Create();

        ecdsa.ImportSubjectPublicKeyInfo(
            Convert.FromBase64String(publicKey),
            out _);

        return ecdsa.VerifyData(
            Encoding.UTF8.GetBytes(data),
            Convert.FromBase64String(signature),
            HashAlgorithmName.SHA256);
    }
}